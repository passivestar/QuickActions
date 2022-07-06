using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.SceneManagement;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Reflection;
using EA = UnityEditor.EditorApplication;

#if (USE_VISUAL_SCRIPTING)
using Unity.VisualScripting;
#endif

public class QuickActions
{
    // ----------------------------------------------------------------
    // Util
    // ----------------------------------------------------------------

    static string scriptTemplate =
        "using UnityEngine;\n\n"
        + "public class __name__ : MonoBehaviour\n"
        + "{\n\n}";

    static Vector3 GetNewObjectPosition()
    {
        if (Selection.activeGameObject != null)
        {
            return Selection.activeGameObject.transform.position;
        }
        else
        {
            var ray = Camera.current.ViewportPointToRay(new Vector3(.5f, .5f, 0));
            bool somethingHit = Physics.Raycast(ray, out var hit, 100f);
            return somethingHit ? hit.point : Vector3.zero;
        }
    }

    static void CreateNewObject(string name = "New Object", Action<GameObject> OnCreate = null)
    {
        var go = new GameObject();
        go.name = name;
        go.transform.position = GetNewObjectPosition();
        if (Selection.activeGameObject != null)
        {
            go.transform.SetParent(Selection.activeGameObject.transform);
        }
        Selection.activeGameObject = go;
        OnCreate?.Invoke(go);
    }

    static void TraverseObject(Transform transform, Action<Transform> OnTraverse = null)
    {
        OnTraverse?.Invoke(transform);
        foreach (Transform childTranform in transform.transform)
        {
            TraverseObject(childTranform, OnTraverse);
        }
    }

    // A lot of useful editor methods are private
    static object InvokeMethodThroughReflection(Type type, string methodName, object instance = null, object[] parameters = null)
    {
        var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        var methodInfo = type.GetMethod(methodName, flags);
        if (methodInfo == null)
        {
            throw new Exception($"Method not found: {methodName}");
        }
        return methodInfo.Invoke(instance, parameters);
    }

    static object InvokeMethodThroughReflection(string typeName, string methodName, object instance = null,object[] parameters = null)
    {
        Type type = GetTypeThroughReflection(typeName);
        return InvokeMethodThroughReflection(type, methodName, instance, parameters);
    }

    // A lot of useful editor classes are private
    static Type GetTypeThroughReflection(string typeName, Type referenceType = null)
    {
        referenceType ??= typeof(UnityEditor.Editor);
        var assembly = Assembly.GetAssembly(referenceType);
        return assembly.GetType(typeName, true);
    }

    static Quaternion SnapToNearestRightAngle(Quaternion rotation)
    {
        Vector3 closestToForward = SnappedToNearestAxis(rotation * Vector3.forward);
        Vector3 closestToUp = SnappedToNearestAxis(rotation * Vector3.up);
        return Quaternion.LookRotation(closestToForward, closestToUp);
    }

    static Vector3 SnappedToNearestAxis(Vector3 direction)
    {
        var x = Mathf.Abs(direction.x);
        var y = Mathf.Abs(direction.y);
        var z = Mathf.Abs(direction.z);
        if (x > y && x > z)
            return new Vector3(Mathf.Sign(direction.x), 0, 0);
        else if (y > x && y > z)
            return new Vector3(0, Mathf.Sign(direction.y), 0);
        else
            return new Vector3(0, 0, Mathf.Sign(direction.z));
    }

    class TextPrompt : EditorWindow
    {
        string currentText = "";

        Action<string> onEnter;

        void OnGUI()
        {
            GUI.SetNextControlName("Prompt");
            currentText = EditorGUILayout.TextField("", currentText);
            if (Event.current.keyCode == KeyCode.Return)
            {
                onEnter?.Invoke(currentText);
                Close();
            }
            if (Event.current.keyCode == KeyCode.Escape) Close();
            EditorGUI.FocusTextInControl("Prompt");
        }

        void OnLostFocus() => Close();

        public static void Prompt(Action<string> onEnter, string defaultText = "")
        {
            var window = ScriptableObject.CreateInstance<TextPrompt>();
            window.onEnter = onEnter;
            window.currentText = defaultText;

            var focusedWindowCenter = EditorWindow.focusedWindow.position.center;
            var width = 300;
            var height = 23;

            window.position = new Rect(
                focusedWindowCenter.x - width / 2,
                focusedWindowCenter.y - height / 2,
                width,
                height
            );
            window.ShowPopup();
        }
    }

    // ----------------------------------------------------------------
    // Object
    // ----------------------------------------------------------------

    [MenuItem("Quick Actions/1 Object/1 Rename")]
    static void Rename()
    {
        var gameObject = Selection.activeGameObject;
        if (gameObject != null)
        {
            TextPrompt.Prompt(text => gameObject.name = text, gameObject.name);
        }
    }

    [MenuItem("Quick Actions/1 Object/2 Reset Position")]
    static void ResetPosition()
    {
        var gameObject = Selection.activeGameObject;
        if (gameObject != null)
        {
            gameObject.transform.localPosition = Vector3.zero;
        }
    }

    [MenuItem("Quick Actions/1 Object/3 Set Static")]
    static void SetStatic()
    {
        if (Selection.activeGameObject != null)
        {
            TraverseObject(Selection.activeGameObject.transform, t =>
            {
                t.gameObject.isStatic = true;
            });
        }
    }

    [MenuItem("Quick Actions/1 Object/4 Create New Empty")]
    static void CreateNewEmpty()
    {
        EA.ExecuteMenuItem("GameObject/Create Empty");

        void OnPrompt(string text)
        {
            var gameObject = Selection.activeGameObject;
            gameObject.name = text;
        }

        TextPrompt.Prompt(OnPrompt, "New Empty");
    }
    
    #if (USE_VISUAL_SCRIPTING)
    [MenuItem("Quick Actions/1 Object/5 Create Embedded Graph")]
    static void CreateEmbeddedGraph()
    {
        var gameObject = Selection.activeGameObject; 
        var scriptMachine = gameObject.AddComponent<ScriptMachine>();
        var graph = FlowGraph.WithStartUpdate();
        scriptMachine.nest.SwitchToEmbed(graph);
        // Force visual scripting panel to refresh:
        EA.ExecuteMenuItem("Edit/Deselect All");
        Selection.activeObject = gameObject;
    }

    [MenuItem("Quick Actions/1 Object/6 Create Graph")]
    static void CreateGraph()
    {
        EA.ExecuteMenuItem("Assets/Create/Visual Scripting/Script Graph");
    }
    #endif

    [MenuItem("Quick Actions/1 Object/7 Create Script")]
    static void CreateScript()
    {
        var gameObject = Selection.activeGameObject;
        if (gameObject == null)
        {
            // Create a script without attaching it to an object
            EA.ExecuteMenuItem("Assets/Create/C# Script");
            return;
        }

        void OnPrompt(string name)
        {
            name = Regex.Replace(name, @"\W", "");

            var relativeFolderPath = "Scripts";
            var absoluteFolderPath = Path.Combine(Application.dataPath, relativeFolderPath);

            if (!Directory.Exists(absoluteFolderPath))
            {
                Directory.CreateDirectory(absoluteFolderPath);
                AssetDatabase.Refresh();
            }

            var relativePath = $"{relativeFolderPath}/{name}.cs";
            var absolutePath = Path.Combine(Application.dataPath, relativePath);

            if (!File.Exists(absolutePath))
            {
                File.WriteAllText(absolutePath, scriptTemplate.Replace("__name__", name));
                AssetDatabase.Refresh();
                var asset = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/" + relativePath);
                InvokeMethodThroughReflection(
                    typeof(InternalEditorUtility),
                    "AddScriptComponentUncheckedUndoable",
                    null,
                    new object[] { gameObject, asset }
                );
            }
            System.Diagnostics.Process.Start(absolutePath);
        }

        var defaultName = Regex.Replace(gameObject.name, @"\W", "");
        TextPrompt.Prompt(OnPrompt, defaultName);
    }

    // ----------------------------------------------------------------
    // Prefab
    // ----------------------------------------------------------------

    [MenuItem("Quick Actions/2 Prefab/1 Make Prefab")]
    static void MakePrefab()
    {
        var prefabsFolderPath = Path.Combine(Application.dataPath, "Prefabs");
        if (!Directory.Exists(prefabsFolderPath))
        {
            Directory.CreateDirectory(prefabsFolderPath);
            AssetDatabase.Refresh();
        }
        var go = Selection.activeGameObject;
        PrefabUtility.SaveAsPrefabAssetAndConnect(go, $"Assets/Prefabs/{go.name}.prefab", InteractionMode.UserAction);
    }

    [MenuItem("Quick Actions/2 Prefab/2 Edit Prefab")]
    static void EditPrefab()
    {
        if (StageUtility.GetCurrentStage().name == "MainStage")
        {
            InvokeMethodThroughReflection(
                "UnityEditor.SceneManagement.PrefabStageUtility",
                "EnterInContextPrefabModeShortcut"
            );
        }
        else
        {
            StageUtility.GoToMainStage();
        }
    }

    [MenuItem("Quick Actions/2 Prefab/3 Apply Overrides")]
    static void ApplyOverrides()
    {
        foreach (var go in Selection.gameObjects)
        {
            PrefabUtility.ApplyPrefabInstance(go, InteractionMode.UserAction);
        }
    }

    [MenuItem("Quick Actions/2 Prefab/4 Revert Overrides")]
    static void RevertOverrides()
    {
        foreach (var go in Selection.gameObjects)
        {
            PrefabUtility.RevertPrefabInstance(go, InteractionMode.UserAction);
        }
    }

    // ----------------------------------------------------------------
    // Viewport
    // ----------------------------------------------------------------

    [MenuItem("Quick Actions/3 Viewport/1 Toggle Overlays")]
    static void ToggleOverlays()
    {
        var view = SceneView.lastActiveSceneView;
        view.drawGizmos = !view.drawGizmos;
    }

    static bool allEffectsEnabled = true;

    [MenuItem("Quick Actions/3 Viewport/2 Toggle Effects")]
    static void ToggleEffects()
    {
        var view = SceneView.lastActiveSceneView;
        var alwaysRefresh = view.sceneViewState.alwaysRefresh;
        allEffectsEnabled = !allEffectsEnabled;
        view.sceneViewState.SetAllEnabled(allEffectsEnabled);
        view.sceneViewState.alwaysRefresh = alwaysRefresh;
        SceneView.RepaintAll();
    }

    [MenuItem("Quick Actions/3 Viewport/3 Shaded")]
    static void Shaded()
    {
        var view = SceneView.lastActiveSceneView;
        view.cameraMode = SceneView.GetBuiltinCameraMode(DrawCameraMode.Textured);
    }

    [MenuItem("Quick Actions/3 Viewport/4 Shaded Wireframe")]
    static void ShadedWireframe()
    {
        var view = SceneView.lastActiveSceneView;
        view.cameraMode = SceneView.GetBuiltinCameraMode(DrawCameraMode.TexturedWire);
    }

    [MenuItem("Quick Actions/3 Viewport/5 Lightmaps")]
    static void Lightmaps()
    {
        var view = SceneView.lastActiveSceneView;
        view.cameraMode = SceneView.GetBuiltinCameraMode(DrawCameraMode.BakedLightmap);
    }

    // ----------------------------------------------------------------
    // Window
    // ----------------------------------------------------------------

    [MenuItem("Quick Actions/4 Window/1 Inspector &1")]
    static void Inspector() => EA.ExecuteMenuItem("Window/General/Inspector");

    [MenuItem("Quick Actions/4 Window/2 Hierarchy &2")]
    static void Hierarchy()
    {
        EA.ExecuteMenuItem("Window/General/Hierarchy");
        Type sceneHierarchyWindowType = GetTypeThroughReflection("UnityEditor.SceneHierarchyWindow");
        if (EditorWindow.focusedWindow.GetType().IsAssignableFrom(sceneHierarchyWindowType))
        {
            InvokeMethodThroughReflection(
                sceneHierarchyWindowType,
                "FrameObject",
                EditorWindow.focusedWindow,
                new object[] { Selection.activeInstanceID, false }
            );
        }
    }

    [MenuItem("Quick Actions/4 Window/3 Project &3")]
    static void Project() => EA.ExecuteMenuItem("Window/General/Project");

    [MenuItem("Quick Actions/4 Window/4 Project Settings")]
    static void ProjectSettings() => EA.ExecuteMenuItem("Edit/Project Settings...");

    [MenuItem("Quick Actions/4 Window/5 Package Manager")]
    static void PackageManager() => EA.ExecuteMenuItem("Window/Package Manager");

    [MenuItem("Quick Actions/4 Window/6 Preferences")]
    static void Preferences() => EA.ExecuteMenuItem("Edit/Preferences...");
    
    [MenuItem("Quick Actions/4 Window/7 Lighting")]
    static void Lighting() => EA.ExecuteMenuItem("Window/Rendering/Lighting");

    [MenuItem("Quick Actions/4 Window/0 Close Floating Panel &4")]
    static void CloseFloatingPanel()
    {
        var window = EditorWindow.focusedWindow;
        if (!window.docked)
        {
            window.Close();
        }
        else
        {
            EA.ExecuteMenuItem("Window/Panels/Close all floating panels...");
        }
    }

    // ----------------------------------------------------------------
    // Other
    // ----------------------------------------------------------------

    [MenuItem("Quick Actions/5 Other/1 Bake Lightmaps")]
    static void BakeLightmaps() => Lightmapping.BakeAsync();

    // ----------------------------------------------------------------
    // General
    // ----------------------------------------------------------------

    [MenuItem("Quick Actions/Q Snap View &s")]
    static void SnapView()
    {
        var view = SceneView.lastActiveSceneView;
        if (view.orthographic)
        {
            view.orthographic = false;
        }
        else
        {
            view.rotation = SnapToNearestRightAngle(view.rotation);
            view.orthographic = true;
        }
        view.Repaint();
    }

    [MenuItem("Quick Actions/W Play Mode &r")]
    static void PlayMode() => EA.ExecuteMenuItem("Edit/Play");

    [MenuItem("Quick Actions/D Delete &d")]
    static void Delete() => EA.ExecuteMenuItem("Edit/Delete");

    [MenuItem("Quick Actions/C Clear Console")]
    static void ClearConsole()
    {
        var type = GetTypeThroughReflection("UnityEditor.LogEntries");
        InvokeMethodThroughReflection(type, "Clear");
    }
}