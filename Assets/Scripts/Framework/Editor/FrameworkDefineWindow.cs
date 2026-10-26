using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// 宏定义管理窗口：按 BuildTargetGroup 查看/增删 scripting define symbols。
/// 说明：微信小游戏相关宏（WEIXINMINIGAME / PLATFORM_WEIXINMINIGAME / UNITY_WEBGL）
/// 由 Tuanjie 子平台与 SDK 自动注入，本窗口用于查看与自定义业务宏。
/// </summary>
public class FrameworkDefineWindow : EditorWindow
{
    private BuildTargetGroup _group;
    private string _editing;

    [MenuItem("Framework/3. Define Manager")]
    public static void Open()
    {
        var window = GetWindow<FrameworkDefineWindow>("宏定义管理");
        window._group = EditorUserBuildSettings.selectedBuildTargetGroup;
        window._editing = GetDefines(window._group);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();
        _group = (BuildTargetGroup)EditorGUILayout.EnumPopup("Build Target Group", _group);

        EditorGUILayout.Space();
        string current = GetDefines(_group);
        EditorGUILayout.LabelField("当前宏定义:", string.IsNullOrEmpty(current) ? "（空）" : current);

        EditorGUILayout.HelpBox("在下方编辑并以分号分隔，Apply 后全量覆盖写入该平台组。", MessageType.Info);
        if (_editing == null) _editing = current;
        _editing = EditorGUILayout.TextField("编辑宏", _editing);

        if (GUILayout.Button("Apply 全量写入", GUILayout.Height(32)))
        {
            SetDefines(_group, _editing);
            Debug.Log($"[DefineWindow] 已写入 {_group}: {_editing}");
            GUI.FocusControl(null);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("常见自动宏参考", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("• UNITY_EDITOR / DEVELOPMENT_BUILD：由构建类型自动注入");
        EditorGUILayout.LabelField("• UNITY_WEBGL / WEIXINMINIGAME / PLATFORM_WEIXINMINIGAME：微信小游戏目标自动注入");
        EditorGUILayout.LabelField("• 平台模块的编译裁剪见 Framework/WeChatPlatform（勿手动增删）");
    }

    /// <summary>读取某平台组的宏定义（供测试/脚本调用）</summary>
    public static string GetDefines(BuildTargetGroup group)
        => PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(group));

    /// <summary>写入某平台组的宏定义（全量覆盖）</summary>
    public static void SetDefines(BuildTargetGroup group, string defines)
        => PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(group), defines);
}
