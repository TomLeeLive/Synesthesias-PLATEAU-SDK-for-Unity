using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PLATEAU.CityExport.ModelConvert;
using PLATEAU.CityImport.Load.Convert;
using PLATEAU.CityInfo;
using PLATEAU.Native;
using PLATEAU.PolygonMesh;
using PLATEAU.Util;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
#endif

namespace PLATEAU.GranularityConvert
{
    public class CityGranularityConverter
    {
        public async Task ConvertAsync(IReadOnlyList<GameObject> srcGameObjs, GranularityConvertOption option)
        {
            try
            {
                using var progressBar = new ProgressBar();
                progressBar.Display("属性情報を取得中...", 0.1f);

                // 属性情報を覚えておきます。
                var attributes = GmlIdToSerializedCityObj.ComposeFrom(srcGameObjs);
                
                // PLATEAUInstancedCityModel が含まれる場合、これもコピーしたいので覚えておきます。
                var instancedCityModelDict = InstancedCityModelDict.ComposeFrom(srcGameObjs); 
                

                progressBar.Display("ゲームオブジェクトを共通モデルに変換中...", 0.2f);

                // ゲームオブジェクトを共通ライブラリのModelに変換します。
                using var srcModel = UnityMeshToDllModelConverter.Convert(srcGameObjs, true, false, ConvertVertex);

                progressBar.Display("共通モデルの変換中...", 0.5f);

                // 共通ライブラリの機能でモデルを分割・結合します。
                var converter = new GranularityConverter();
                using var dstModel = converter.Convert(srcModel, option);

                progressBar.Display("変換後の3Dモデルを配置中...", 0.8f);

                // Modelをゲームオブジェクトに変換して配置します。
                var commonParent = CalcCommonParent(srcGameObjs.Select(obj => obj.transform).ToArray());
                var infoForToolkits = new CityObjectGroupInfoForToolkits(true);
                var result = await PlateauToUnityModelConverter.PlateauModelToScene(
                    commonParent, new DummyProgressDisplay(), "", true,
                    null, null, dstModel,
                    new AttributeDataHelper(new SerializedCityObjectGetterFromDict(attributes), option.Granularity,
                        true), true, infoForToolkits);
                if (!result.IsSucceed)
                {
                    throw new Exception("Failed to convert plateau model to scene game objects.");
                }

                if (result.RootObjs.Count <= 0)
                {
                    Dialogue.Display("変換対象がありません。\nアクティブなオブジェクトを選択してください。", "OK");
                    return;
                }
                
                // PLATEAUInstancedCityModelを復元します。
                instancedCityModelDict.Restore(result.RootObjs);

                if (Dialogue.Display("変換前のゲームオブジェクトを削除しますか？ 残しますか？", "削除", "残す"))
                {
                    foreach (var srcObj in srcGameObjs)
                    {
                        Object.DestroyImmediate(srcObj);
                    }
                }
                
#if UNITY_EDITOR
                // 変換後のゲームオブジェクトを選択状態にします。
                Selection.objects = result.RootObjs.Select(go => (Object)go).ToArray();
#endif
            }
            catch (Exception e)
            {
                Debug.LogError($"{e.Message}\n{e.StackTrace}");
            }
            
        }
        
        private static PlateauVector3d ConvertVertex(Vector3 src)
        {
            return new PlateauVector3d(src.x, src.y, src.z);
        }

         /// <summary>
         /// 引数の共通の親を探し、親のうちもっとも階層上の子であるものを返します。
         /// 共通の親がない場合、nullを返します。
         /// </summary>
        private static Transform CalcCommonParent(IReadOnlyList<Transform> srcList)
         {
             // 各親が、srcListのうちいくつの親であるかを数えます。
             Dictionary<Transform, int> descendantCountDict = new();
             foreach (var src in srcList)
             {
                 var parent = src.parent;
                 // 親をたどりながら子孫カウントをインクリメントします。
                 while (parent != null)
                 {
                     if (descendantCountDict.ContainsKey(parent))
                     {
                         descendantCountDict[parent]++;
                     }
                     else
                     {
                         descendantCountDict.Add(parent, 1);
                     }
                     parent = parent.parent;
                 }
             }
             
             if (descendantCountDict.Count == 0) return null;
             
             var commonParents = descendantCountDict
                 .Where(pair => pair.Value == srcList.Count)
                 .Select(pair => pair.Key)
                 .ToArray();
             if (commonParents.Length == 0) return null;
             
             // 共通の親のうち、もっとも子であるものを探します。
             for (int i = 0; i < commonParents.Length; i++)
             {
                 var trans1 = commonParents[i];
                 bool isTrans1Parented = false;
                 for (int j = i + 1; j < commonParents.Length; j++)
                 {
                     var trans2 = commonParents[j];
                     if (trans2.IsChildOf(trans1))
                     {
                         isTrans1Parented = true;
                         break;
                     }
                 }

                 if (!isTrans1Parented) return trans1;
             }

             throw new Exception("Failed to search common parent.");
         }
    }


    public class SerializedCityObjectGetterFromDict : ISerializedCityObjectGetter
    {
        private GmlIdToSerializedCityObj data;

        public SerializedCityObjectGetterFromDict(GmlIdToSerializedCityObj dict)
        {
            data = dict;
        }

        public CityInfo.CityObjectList.CityObject GetByID(string gmlID, CityObjectIndex? _)
        {
            if (data.TryGet(gmlID, out var serializedCityObj))
            {
                return serializedCityObj;
            }
            else
            {
                Debug.LogWarning($"gmlID not found : {gmlID}");
                return null;
            }
        }

        public void Dispose()
        {
            // NOP
        }
    }
}