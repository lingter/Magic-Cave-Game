using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MagicCave.Prototype.Editor
{
    public static class PrototypeSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/MagicCavePrototype.unity";

        [MenuItem("Magic Cave/Create Playable Prototype Scene")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            System.IO.Directory.CreateDirectory("Assets/MagicCavePrototype/Materials");
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            AssetDatabase.Refresh();
            var lit = MaterialAsset("Surface", "Universal Render Pipeline/Lit", false);
            var unlit = MaterialAsset("Fog", "Universal Render Pipeline/Unlit", false);
            var transparent = MaterialAsset("FadedWall", "Universal Render Pipeline/Lit", true);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.rotation = Quaternion.Euler(50, 0, 0);
            camera.transform.position = -camera.transform.forward * 10;
            camera.fieldOfView = 35;
            camera.nearClipPlane = 0.1f; camera.farClipPlane = 80;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.015f, 0.025f, 0.045f);
            camera.allowHDR = false;
            camera.gameObject.AddComponent<AudioListener>();
            var light = new GameObject("Cave Key Light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.25f;
            light.transform.rotation = Quaternion.Euler(55, -25, 0);
            light.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.48f, 0.55f, 0.68f);
            var game = new GameObject("Magic Cave Prototype").AddComponent<PrototypeGame>();
            game.surfaceTemplate = lit; game.unlitTemplate = unlit; game.transparentTemplate = transparent;
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = game.gameObject;
            Debug.Log("[MagicCave] Playable scene ready: " + ScenePath);
        }

        static Material MaterialAsset(string name, string shaderName, bool transparent)
        {
            string path = "Assets/MagicCavePrototype/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.InvalidOperationException("Missing shader: " + shaderName);
            material = new Material(shader) { name = name };
            if (transparent)
            {
                material.SetFloat("_Surface", 1);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
                material.SetColor("_BaseColor", new Color(0.3f, 0.4f, 0.5f, 0.22f));
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
