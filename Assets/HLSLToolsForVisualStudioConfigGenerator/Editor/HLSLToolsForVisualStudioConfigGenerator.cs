/*
MIT License

Copyright (c) 2020 hecomi

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections.Generic;

namespace HLSLToolsForVisualStudioConfigGenerator
{

internal class PackageInfo
{
    public string name;
    public string version;
    public string source;
    public string dir;
    public bool use = true;
    public string label => $"{name} ({source}: {version})";
}

internal static class Common
{
    public static List<string> hlslPackages = new List<string>
    {
        "com.unity.render-pipelines.core",
        "com.unity.render-pipelines.high-definition",
        "com.unity.render-pipelines.universal",
        "com.unity.shadergraph",
    };
}

public class Window : ScriptableWizard
{
    const string windowName = "HLSL Tools for Visual Studio Config Generator";

    [MenuItem("Window/" + windowName)]
    static void Open()
    {
        var window = DisplayWizard<Window>(windowName, "Close", "Create");
        window.UpdatePackages();
        window.Show();
    }

    protected override bool DrawWizardGUI()
    {
        DrawSettings();
        EditorGUILayout.Space();
        UpdateExportInfo();
        DrawExport();
        EditorGUILayout.Space();
        DrawPackages();
        DrawInfo();
        return true;
    }

    void OnWizardCreate()
    {
        Close();
    }

    void OnWizardOtherButton()
    {
        try
        {
            Export();
        }
        catch (Exception e)
        {
            errorMessage = e.Message;
            EditorUtility.ClearProgressBar();
        }
    }

    // ---

    Vector2 packagesScrollPos = Vector2.zero;
    List<PackageInfo> packages = new List<PackageInfo>();
    string symbolicLinkDirectory = "ShaderIncludes";
    bool exportProjectDir = true;
    bool exportCgIncludeDir = true;
    bool exportPackageDir = true;
    bool doesConfigJsonExist = false;
    bool doesSymbolicLinkDirExist = false;

    string infoMessage = "";
    string errorMessage = "";

    string rootDirFullPath
    {
        get { return Directory.GetParent(Application.dataPath).FullName; }
    }

    string packagesLockJsonFullPath
    {
        get { return Path.Combine(rootDirFullPath, "Packages", "packages-lock.json"); }
    }

    string configJsonFullPath
    {
        get { return Path.Combine(rootDirFullPath, "shadertoolsconfig.json"); }
    }

    string symbolicLinkDirectoryFullPath
    {
        get { return Path.Combine(rootDirFullPath, symbolicLinkDirectory); }
    }

    string originalPackageDirectoryFullPath
    {
        get { return Path.Combine(rootDirFullPath, "Library", "PackageCache"); }
    }

    string cgIncludesDirectoryFullPath
    {
        get
        {
            var appPath = Environment.GetCommandLineArgs()[0];
            var appDir = Path.GetDirectoryName(appPath);
            return Path.Combine(appDir, "Data", "CGIncludes");
        }
    }

    void UpdatePackages()
    {
        packages.Clear();

        string jsonStr;
        try
        {
            jsonStr = File.ReadAllText(packagesLockJsonFullPath);
        }
        catch (Exception e)
        {
            errorMessage = e.Message;
            return;
        }

        var dict = MiniJSON.Json.Deserialize(jsonStr) as Dictionary<string, object>;
        if (dict == null) return;

        var deps = dict["dependencies"] as Dictionary<string, object>;
        if (deps == null) return;

        foreach (var kv in deps)
        {
            var name = kv.Key;
            var pkg = kv.Value as Dictionary<string, object>;
            var source = pkg["source"] as string;
            var ver = pkg["version"] as string;
            var dir = $"{name}@{ver}";
            bool use = false;

            if (source.Equals("local"))
            {
                continue;
            }
            else if (source.Equals("git"))
            {
                var hash = pkg["hash"] as string;
                dir = $"{name}@{hash.Substring(0, 10)}";
                use = true;
            }
            else if (source.Equals("builtin"))
            {
                use = Common.hlslPackages.Contains(name);
            }

            packages.Add(new PackageInfo 
            { 
                name = name, 
                version = ver, 
                source = source,
                dir = dir,
                use = use,
            });
        }
    }

    string OpenDialogToSelectSymbolicLinkPath()
    {
        return EditorUtility.OpenFolderPanel(
            "Select directory path to create symbolic links",
            rootDirFullPath,
            string.Empty);
    }

    void DrawSettings()
    {
        EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
        ++EditorGUI.indentLevel;
        {
            exportProjectDir = EditorGUILayout.Toggle("Include Project", exportProjectDir);
            exportCgIncludeDir = EditorGUILayout.Toggle("Include CGInclude", exportCgIncludeDir);
            exportPackageDir = EditorGUILayout.Toggle("Include Packages", exportPackageDir);

            EditorGUILayout.BeginHorizontal();
            {
                symbolicLinkDirectory = EditorGUILayout.TextField(
                    new GUIContent(
                        "Symbolic Link Path", 
                        "Create symbolic links of all installed packages in this directory."),
                    symbolicLinkDirectory);
                var layout = GUILayout.Width(25f);
                if (GUILayout.Button("...", layout))
                {
                    var path = OpenDialogToSelectSymbolicLinkPath();
                    if (!string.IsNullOrEmpty(path))
                    {
                        symbolicLinkDirectory = path;
                        Repaint();
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }
        --EditorGUI.indentLevel;
    }

    void UpdateExportInfo()
    {
        doesConfigJsonExist = File.Exists(configJsonFullPath);
        doesSymbolicLinkDirExist = Directory.Exists(symbolicLinkDirectoryFullPath);
    }

    void DrawExport()
    {
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        ++EditorGUI.indentLevel;
        {
            var buttonLayout = GUILayout.Width(48f);
            var labelStyleGray = new GUIStyle(EditorStyles.label);
            labelStyleGray.normal.textColor = Color.gray;

            EditorGUILayout.BeginHorizontal();
            {
                if (doesConfigJsonExist)
                {
                    EditorGUILayout.LabelField("Config Json", configJsonFullPath);

                    if (GUILayout.Button("Delete", buttonLayout))
                    {
                        DeleteConfigJson();
                    }

                    if (GUILayout.Button("Open", buttonLayout))
                    {
                        System.Diagnostics.Process.Start(configJsonFullPath);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("Config Json", configJsonFullPath, labelStyleGray);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            {
                if (doesSymbolicLinkDirExist)
                {
                    EditorGUILayout.LabelField("Symbolic Link", symbolicLinkDirectoryFullPath);

                    if (GUILayout.Button("Delete", buttonLayout))
                    {
                        bool isDeleteConfirmed = EditorUtility.DisplayDialog(
                            windowName,
                            "Are you sure you want to delete \"" + symbolicLinkDirectoryFullPath + "\"?",
                            "Delete",
                            "Cancel");
                        if (isDeleteConfirmed)
                        {
                            DeleteSymbolicLinks();
                        }
                    }

                    if (GUILayout.Button("Open", buttonLayout))
                    {
                        System.Diagnostics.Process.Start(symbolicLinkDirectoryFullPath);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("Symbolic Link", symbolicLinkDirectoryFullPath, labelStyleGray);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.LabelField("Unity Directory", cgIncludesDirectoryFullPath);
                if (GUILayout.Button("Open", buttonLayout))
                {
                    System.Diagnostics.Process.Start(cgIncludesDirectoryFullPath);
                }
            }
            EditorGUILayout.EndHorizontal();
        }
        --EditorGUI.indentLevel;
    }

    void DrawPackages()
    {
        if (packages.Count == 0)
        {
            UpdatePackages();
        }

        EditorGUILayout.LabelField("Packages", EditorStyles.boldLabel);
        ++EditorGUI.indentLevel;
        {
            packagesScrollPos = EditorGUILayout.BeginScrollView(packagesScrollPos);
            {
                var toggleLayout = GUILayout.Width(20f);
                var openButtonLayout = GUILayout.Width(48f);

                foreach (var pkg in packages)
                {
                    EditorGUILayout.BeginHorizontal();
                    {
                        pkg.use = EditorGUILayout.Toggle(pkg.use, toggleLayout);

                        EditorGUILayout.LabelField(pkg.label);

                        if (GUILayout.Button("Open", openButtonLayout))
                        {
                            var origDir = Path.Combine(originalPackageDirectoryFullPath, pkg.dir);
                            System.Diagnostics.Process.Start(origDir);
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndScrollView();
        }
        --EditorGUI.indentLevel;
    }

    void DrawInfo()
    {
        if (string.IsNullOrEmpty(infoMessage) && string.IsNullOrEmpty(errorMessage)) return;

        EditorGUILayout.Space();

        if (!string.IsNullOrEmpty(infoMessage))
        {
            EditorGUILayout.HelpBox(infoMessage, MessageType.Info);
        }

        if (!string.IsNullOrEmpty(errorMessage))
        {
            EditorGUILayout.HelpBox(errorMessage, MessageType.Error);
        }
    }

    void Export()
    {
        infoMessage = "";
        errorMessage = "";

        EditorUtility.DisplayProgressBar(windowName, "Exporting...", 0f);
        if (exportPackageDir) CreateSymbolicLinks();
        ExportConfigJson();
        EditorUtility.ClearProgressBar();
    }

    void CreateSymbolicLinks()
    {
        var dirPath = Path.Combine(symbolicLinkDirectoryFullPath, "Packages/");
        Directory.CreateDirectory(dirPath);

        int i = 0, n = packages.Count + 1;
        foreach (var pkg in packages)
        {
            if (!pkg.use) continue;
            
            var origDir = Path.Combine(originalPackageDirectoryFullPath, pkg.dir);
            var symLink = Path.Combine(dirPath, pkg.name);
            var cmd = $"mklink /d \"{symLink}\" \"{origDir}\"";
            var proc = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c " + cmd);
            proc.CreateNoWindow = true;
            proc.UseShellExecute = false;
            System.Diagnostics.Process.Start(proc).WaitForExit();

            EditorUtility.DisplayProgressBar(windowName, pkg.dir, (float)++i / n);
        }
    }

    void ExportConfigJson()
    {
        EditorUtility.DisplayProgressBar(windowName, "shadertoolsconfig.json", 1f);

        var defs = new Dictionary<string, object>();
        // TODO: add definitions if needed

        var dirs = new List<string>();
        if (exportProjectDir) dirs.Add(".");
        if (exportCgIncludeDir) dirs.Add(cgIncludesDirectoryFullPath);
        if (exportPackageDir) dirs.Add(symbolicLinkDirectory);

        var root = new Dictionary<string, object>();
        root.Add("hlsl.preprocessorDefinitions", defs);
        root.Add("hlsl.additionalIncludeDirectories", dirs);

        var jsonStr = MiniJSON.Json.Serialize(root);

        using (var stream = new StreamWriter(configJsonFullPath, false, System.Text.Encoding.UTF8))
        {
            stream.Write(jsonStr);
        }

        infoMessage = "Exported!";
    }

    void DeleteSymbolicLinks()
    {
        try
        {
            var packagesPath = Path.Combine(symbolicLinkDirectoryFullPath, "Packages/");
            var symbolicLinks = Directory.GetDirectories(packagesPath);

            int i = 0, n = symbolicLinks.Length;
            foreach (var symLink in symbolicLinks)
            {
                var cmd = string.Format(@"rmdir ""{0}""", symLink);
                var proc = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c " + cmd);
                proc.CreateNoWindow = true;
                proc.UseShellExecute = false;
                System.Diagnostics.Process.Start(proc).WaitForExit();

                EditorUtility.DisplayProgressBar(windowName, symLink, (float)++i / n);
            }
            EditorUtility.ClearProgressBar();

            Debug.Log(symbolicLinkDirectoryFullPath);
            Directory.Delete(symbolicLinkDirectoryFullPath, true);
        }
        catch (Exception e)
        {
            errorMessage = e.Message;
        }
    }

    void DeleteConfigJson()
    {
        try
        {
            File.Delete(configJsonFullPath);
        }
        catch (Exception e)
        {
            errorMessage = e.Message;
        }
    }
}

}

#endif
