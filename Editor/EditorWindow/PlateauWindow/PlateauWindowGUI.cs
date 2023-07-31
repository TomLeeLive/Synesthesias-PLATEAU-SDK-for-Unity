﻿using PLATEAU.Editor.EditorWindow.Common;
using PLATEAU.Editor.EditorWindow.PlateauWindow.MainTabGUI;
using System;

namespace PLATEAU.Editor.EditorWindow.PlateauWindow
{
    internal class PlateauWindowGUI : IEditorDrawable
    {
        public Action<int> OnTabChange;
        public int tabIndex { get => _tabIndex; 
            private set { 
                if(value != _tabIndex)
                    OnTabChange?.Invoke(value);  
                _tabIndex = value;
            } 
        }
        private int _tabIndex = 0;
        private readonly IEditorDrawable[] tabGUIArray;
       
        private readonly string[] tabImages =
            { "dark_icon_import.png", "dark_icon_adjust.png", "dark_icon_export.png", "dark_icon_information.png" };

        public PlateauWindowGUI(UnityEditor.EditorWindow parentEditorWindow)
        {
            this.tabGUIArray = new IEditorDrawable[]
            {
                new CityAddGUI(parentEditorWindow),
                new CityAdjustGUI(),
                new CityExportGUI(),
                new CityAttributeGUI(parentEditorWindow, this)
            };
        }

        public void Draw()
        {
            // ウィンドウのメインとなるタブ選択GUIを表示し、選択中のタブGUIクラスに描画処理を委譲します。
            this.tabIndex = PlateauEditorStyle.TabWithImages(this.tabIndex, this.tabImages, 80);
            PlateauEditorStyle.MainLogo();
            this.tabGUIArray[this.tabIndex].Draw();
        }

        /// <summary> テストからアクセスする用 </summary>
        internal const string NameOfTabIndex = nameof(_tabIndex);

        internal const string NameOfTabGUIArray = nameof(tabGUIArray);
    }
}
