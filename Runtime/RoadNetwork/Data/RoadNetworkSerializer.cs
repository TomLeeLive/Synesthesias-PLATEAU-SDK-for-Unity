﻿using PLATEAU.Util;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;

namespace PLATEAU.RoadNetwork.Data
{
    /// <summary>
    /// シリアライズするときに
    /// </summary>
    [AttributeUsageAttribute(System.AttributeTargets.Class | System.AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public class RoadNetworkSerializeDataAttribute : Attribute
    {
        public Type DataType { get; set; }

        public RoadNetworkSerializeDataAttribute(Type dataType)
        {
            DataType = dataType;
        }
    }

    [AttributeUsageAttribute(System.AttributeTargets.Field | System.AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class RoadNetworkSerializeMemberAttribute : Attribute
    {
        public string FieldName { get; }

        public RoadNetworkSerializeMemberAttribute(string fieldName)
        {
            FieldName = fieldName;
        }

        public RoadNetworkSerializeMemberAttribute()
        {
            FieldName = "";
        }
    }

    public class RoadNetworkSerializer
    {
        private class MemberReference
        {
            public Type SrcType { get; set; }

            public Type DstType { get; set; }

            // key : dst MemberInfo
            // value : src member info
            public Dictionary<FieldInfo, FieldInfo> Dst2SrcMemberTable { get; set; }

            private MemberReference(Type srcType, Type dstType, Dictionary<FieldInfo, FieldInfo> dst2SrcMemberTable)
            {
                SrcType = srcType;
                DstType = dstType;
                Dst2SrcMemberTable = dst2SrcMemberTable;
            }

            public static MemberReference Create(Type dstType)
            {
                var srcType = dstType.GetCustomAttribute<RoadNetworkSerializeDataAttribute>()?.DataType;
                if (srcType == null)
                    throw new ArgumentException(
                        $"{dstType} has not attribute {nameof(RoadNetworkSerializeDataAttribute)}");
                var flags = BindingFlags.Instance | BindingFlags.Public;
                var dst2Src
                    // #NOTE : Field or Propertyのみ対応
                    = dstType.GetProperties(flags)
                        .Concat(dstType.GetFields(flags).Cast<MemberInfo>())
                        // アトリビュート指定されている物を抽出
                        .Select(p => new { member = p, attr = p.GetCustomAttribute<RoadNetworkSerializeMemberAttribute>() })
                        .Where(p => p.attr != null)
                        .ToDictionary(m =>
                        {
                            if (m.member is PropertyInfo p)
                                return TypeUtil.GetPropertyBackingField(dstType, p);
                            return m.member as FieldInfo;
                        }, p =>
                        {
                            var prop = srcType.GetProperty(p.attr.FieldName);
                            if (prop != null)
                            {
                                var ret = TypeUtil.GetPropertyBackingField(srcType, prop);
                                if (ret == null)
                                    throw new InvalidDataException($"Property {prop.Name} has no field info");
                                return ret;
                            }
                            var field = srcType.GetField(p.attr.FieldName);
                            if (field != null)
                                return field;
                            return null;
                        });
                return new MemberReference(srcType, dstType, dst2Src);
            }

            public MemberReference GetReversed()
            {
                return new MemberReference(DstType, SrcType, Dst2SrcMemberTable.ToDictionary(i => i.Value, i => i.Key));
            }
        }

        private interface IDataConverter
        {
            /// <summary>
            /// typeがコンバート可能かどうか
            /// </summary>
            /// <param name="type"></param>
            /// <returns></returns>
            bool Contains(Type type);

            // 
            /// <summary>
            /// コンバート. srcはtype型である必要がある.
            /// srcがnullの事を考えてtypeも別途渡す
            /// </summary>
            /// <param name="type"></param>
            /// <param name="src"></param>
            /// <returns></returns>
            object Convert(Type type, object src);
        }

        private interface IValueConverter
        {
            object Convert(object val);
        }

        private class Object2RnIdConverter<TData> : IValueConverter
            where TData : IPrimitiveData
        {
            private Dictionary<object, RnID<TData>> Table { get; }

            public Object2RnIdConverter(Dictionary<object, RnID<TData>> table)
            {
                Table = table;
            }

            public object Convert(object val)
            {
                if (val == null)
                {
                    return new RnID<TData>();
                }

                return Table[val];
            }
        }

        /// <summary>
        /// RnIdから元のデータを引っ張ってくる
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TData"></typeparam>
        private class RnId2ObjectConverter<TData> : IValueConverter
            where TData : IPrimitiveData
        {
            private Dictionary<RnID<TData>, object> Table { get; }

            public RnId2ObjectConverter(Dictionary<RnID<TData>, object> table)
            {
                Table = table;
            }

            public object Convert(object val)
            {
                if (val is RnID<TData> id)
                {
                    if (id.IsValid == false)
                        return null;
                    return Table[id];
                }

                return null;
            }
        }

        private interface IDataStorage
        {
            void ConvertAll(IDataConverter converter);
        }

        private class DataStorage : IDataStorage
        {
            private Dictionary<object, object> DataTable { get; set; }

            public MemberReference MemberTable { get; }

            public DataStorage(Dictionary<object, object> dataTable, MemberReference memberReference)
            {
                DataTable = dataTable;
                MemberTable = memberReference;
            }

            public void ConvertAll(IDataConverter converter)
            {
                foreach (var item in DataTable)
                {
                    Convert(item.Key, item.Value, MemberTable, converter);
                }
            }
        }


        private class ReferenceTable : IDataConverter
        {
            private Dictionary<Type, IValueConverter> IdConverter { get; } = new();

            private List<DataStorage> Storage { get; } = new List<DataStorage>();


            public void AddStorage(DataStorage dataStorage)
            {
                Storage.Add(dataStorage);
            }

            public void AddConverter(Type srcType, IValueConverter valueConverter)
            {
                IdConverter[srcType] = valueConverter;
            }

            /// <summary>
            /// src -> dstへデータ変換
            /// </summary>
            public void ConvertAll()
            {
                foreach (var d in Storage)
                    d.ConvertAll(this);
            }

            public bool Contains(Type type)
            {
                return IdConverter.ContainsKey(type);
            }

            public object Convert(Type type, object src)
            {
                var idConverter = IdConverter[type];
                return idConverter.Convert(src);
            }
        }


        private static void Convert(object src, object dst, MemberReference memberReference, IDataConverter converter)
        {
            foreach (var m in memberReference.Dst2SrcMemberTable)
            {
                var dstMemberInfo = m.Key;
                var srcMemberInfo = m.Value; ;
                var srcMemberType = TypeUtil.GetMemberType(srcMemberInfo);
                var dstMemberType = TypeUtil.GetMemberType(dstMemberInfo);

                var srcValue = TypeUtil.GetValue(srcMemberInfo, src);
                if (srcMemberType == dstMemberType)
                {
                    TypeUtil.SetValue(dstMemberInfo, dst, srcValue);
                }
                else if (converter.Contains(srcMemberType))
                {
                    var dstValue = converter.Convert(srcMemberType, srcValue);
                    TypeUtil.SetValue(dstMemberInfo, dst, dstValue);
                }
                // #TODO
                // 配列は一旦サポート外
                else if (srcMemberType.IsArray && dstMemberType.IsArray)
                {
                    throw new NotSupportedException($"{srcMemberType.FullName} is not supported serialize");
                }
                // List
                else if (srcMemberType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    var dstObj = dstMemberType.Assembly.CreateInstance(dstMemberType.FullName);
                    var addMethod = dstMemberType.GetMethod(nameof(List<int>.Add));
                    if (srcValue is IEnumerable enumerable)
                    {
                        foreach (var srcV in enumerable)
                        {
                            var dstValue = converter.Convert(srcMemberType.GenericTypeArguments[0], srcV);
                            addMethod.Invoke(dstObj, new[] { dstValue });
                        }
                    }
                    TypeUtil.SetValue(dstMemberInfo, dst, dstObj);

                }
                else
                {
                    throw new NotSupportedException($"{srcMemberType.FullName} is not supported serialize");
                }
            }
        }

        private void Collect<TData>(ReferenceTable refTable, RoadNetworkModel model, PrimitiveDataStorage.PrimitiveStorage<TData> storage)
            where TData : IPrimitiveData, new()
        {
            var memberReference = MemberReference.Create(typeof(TData));

            // TSrcの型のインスタンスを全部探してくる
            var src = TypeUtil
                .GetAllMembersRecursively(model, memberReference.SrcType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(x => x.Item2)
                .Where(x => x != null)
                .Distinct()
                .ToList();

            // 変換後のデータのnewだけ行う
            var dataList = Enumerable.Range(0, src.Count).Select(x => new TData()).ToArray();

            var ids = storage.WriteNew(dataList);

            var obj2Id = Enumerable.Range(0, ids.Length)
                .ToDictionary(i => src[i], i => ids[i]);

            var valueConverter =
                new Object2RnIdConverter<TData>(obj2Id);

            var obj2Data = Enumerable.Range(0, src.Count)
                .ToDictionary(i => src[i], i => dataList[i] as object);

            var dataStorage = new DataStorage(obj2Data, memberReference);
            refTable.AddStorage(dataStorage);
            refTable.AddConverter(memberReference.SrcType, valueConverter);
        }

        public RoadNetworkStorage Serialize(RoadNetworkModel roadNetworkModel)
        {
            var ret = new RoadNetworkStorage();
            var refTable = new ReferenceTable();
            Collect(refTable, roadNetworkModel, ret.PrimitiveDataStorage.Points);
            Collect(refTable, roadNetworkModel, ret.PrimitiveDataStorage.LineStrings);
            Collect(refTable, roadNetworkModel, ret.PrimitiveDataStorage.Links);
            Collect(refTable, roadNetworkModel, ret.PrimitiveDataStorage.Lanes);
            Collect(refTable, roadNetworkModel, ret.PrimitiveDataStorage.Tracks);
            Collect(refTable, roadNetworkModel, ret.PrimitiveDataStorage.Blocks);
            Collect(refTable, roadNetworkModel, ret.PrimitiveDataStorage.Nodes);
            Collect(refTable, roadNetworkModel, ret.PrimitiveDataStorage.Ways);

            refTable.ConvertAll();
            return ret;
        }

        private List<T> Collect<TData, T>(ReferenceTable refTable, PrimitiveDataStorage.PrimitiveStorage<TData> storage)
            where TData : IPrimitiveData
            where T : new()
        {
            var memberReference = MemberReference.Create(typeof(TData)).GetReversed();
            // 先にデータを作成する
            var objList = Enumerable.Range(0, storage.DataList.Count).Select(i => new T()).ToList();

            var id2Obj = Enumerable.Range(0, storage.DataList.Count)
                .ToDictionary(i => new RnID<TData>(i), i => objList[i] as object);

            var idConverter = new RnId2ObjectConverter<TData>(id2Obj);

            var data2Obj = Enumerable.Range(0, storage.DataList.Count)
                .ToDictionary(i => storage.DataList[i] as object, i => objList[i] as object);
            var dataStorage = new DataStorage(data2Obj, memberReference);
            refTable.AddStorage(dataStorage);
            refTable.AddConverter(typeof(RnID<TData>), idConverter);
            return objList;
        }

        public RoadNetworkModel Deserialize(RoadNetworkStorage roadNetworkStorage)
        {
            var refTable = new ReferenceTable();
            var points = Collect<RoadNetworkDataPoint, RoadNetworkPoint>(refTable, roadNetworkStorage.PrimitiveDataStorage.Points);
            var tracks = Collect<RoadNetworkDataTrack, RoadNetworkTrack>(refTable, roadNetworkStorage.PrimitiveDataStorage.Tracks);
            var nodes = Collect<RoadNetworkDataNode, RoadNetworkNode>(refTable, roadNetworkStorage.PrimitiveDataStorage.Nodes);
            var links = Collect<RoadNetworkDataLink, RoadNetworkLink>(refTable, roadNetworkStorage.PrimitiveDataStorage.Links);
            var lineStrings = Collect<RoadNetworkDataLineString, RoadNetworkLineString>(refTable, roadNetworkStorage.PrimitiveDataStorage.LineStrings);
            var blocks = Collect<RoadNetworkDataBlock, RoadNetworkBlock>(refTable, roadNetworkStorage.PrimitiveDataStorage.Blocks);
            var ways = Collect<RoadNetworkDataWay, RoadNetworkWay>(refTable, roadNetworkStorage.PrimitiveDataStorage.Ways);
            var lanes = Collect<RoadNetworkDataLane, RoadNetworkLane>(refTable, roadNetworkStorage.PrimitiveDataStorage.Lanes);

            refTable.ConvertAll();
            var ret = new RoadNetworkModel();
            ret.Links.AddRange(links);
            ret.Nodes.AddRange(nodes);
            return ret;
        }
    }
}