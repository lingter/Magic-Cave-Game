using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using MagicCave.Arknights;
using Object = UnityEngine.Object;

/// <summary>Editable, entirely mesh-based reconstruction of 0-1 Collapse.</summary>
public static class Arknights01SceneBuilder
{
    const string Folder = "Assets/Arknights01";
    const string RootName = "Arknights 0-1 · Collapse";
    static Transform root, environment;
    static Material concrete, asphalt, pale, steel, dark, orange, yellow, paper, blue, red;
    static System.Random random;
    // Rows shown north to south; ground truth: level_main_00-01.json mapData.
    static readonly string[] Map = { "FFFFFFFFF", "FHHHHHHHF", "GRHRRRRRF", "FRRRHRRRE", "FHHHHHHHF", "FFFFFFFFF" };

    [MenuItem("Tools/Arknights/Build 0-1 Collapse")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Build the map in Edit Mode.");
        Directory.CreateDirectory(Folder + "/Materials");
        Directory.CreateDirectory(Folder + "/Textures");
        Directory.CreateDirectory(Folder + "/Prefabs");
        AssetDatabase.Refresh();
        random = new System.Random(101);
        var previous = GameObject.Find(RootName);
        if (previous) Undo.DestroyObjectImmediate(previous);
        root = new GameObject(RootName).transform;
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Build Arknights 0-1");
        CreateMaterials();
        var grid = Group("01 · Tactical grid — 9 x 6", root);
        var layout = root.gameObject.AddComponent<ArknightsMapLayout>();
        layout.reference = "0-1 坍塌 / Collapse\nLayout: Kengxxiao/ArknightsGameData zh_CN/gamedata/levels/obt/main/level_main_00-01.json\nVisual: https://prts.wiki/w/0-1_坍塌\nOriginal game coordinates: row 0 = south; column 0 = west. 1 tile = 2 Unity units.";
        var tiles = new List<ArknightsMapLayout.Tile>();
        GameObject groundPrefab = null, highPrefab = null;
        for (int topRow = 0; topRow < 6; topRow++)
        for (int col = 0; col < 9; col++)
        {
            int row = 5 - topRow;
            char key = Map[topRow][col];
            var kind = key == 'H' ? ArknightsMapLayout.TileKind.Highland : key == 'R' ? ArknightsMapLayout.TileKind.Road : key == 'E' ? ArknightsMapLayout.TileKind.Entry : key == 'G' ? ArknightsMapLayout.TileKind.Goal : ArknightsMapLayout.TileKind.Forbidden;
            var cell = Group(string.Format("Tile [{0},{1}] · {2}", row, col, kind), grid);
            cell.localPosition = new Vector3((col - 4) * 2, 0, (row - 2.5f) * 2);
            if (key == 'H')
            {
                Cube("Concrete riser", cell, new Vector3(0, 0.19f, 0), new Vector3(1.97f, 0.5f, 1.97f), concrete);
                Cube("Steel fascia", cell, new Vector3(0, 0.40f, 0), new Vector3(1.985f, 0.13f, 1.985f), steel);
                Cube("Pale deployment slab", cell, new Vector3(0, 0.50f, 0), new Vector3(1.91f, 0.10f, 1.91f), pale);
                Cube("Front inset lip", cell, new Vector3(0, 0.34f, -0.995f), new Vector3(1.8f, 0.045f, 0.025f), orange);
                foreach (float x in new[] { -0.85f, 0.85f })
                foreach (float z in new[] { -0.85f, 0.85f })
                    Cube("Recessed anchor", cell, new Vector3(x, 0.554f, z), new Vector3(0.035f, 0.008f, 0.035f), dark, false);
                if (highPrefab == null) highPrefab = SaveTilePrefab(cell, "HighlandTile");
            }
            else
            {
                var surface = Cube("Road surface", cell, new Vector3(0, -0.13f, 0), new Vector3(1.99f, 0.25f, 1.99f), key == 'F' ? concrete : asphalt);
                if (key == 'R' && groundPrefab == null) groundPrefab = SaveTilePrefab(cell, "GroundTile");
                if (key == 'R')
                {
                    for (int i = 0; i < 4; i++)
                        Cube("Broken grid seam", cell, new Vector3(-0.997f, 0.002f, -0.73f + i * 0.48f), new Vector3(0.021f, 0.009f, 0.09f), dark, false);
                    if ((row + col) % 3 == 0) Crack(cell);
                }
            }
            tiles.Add(new ArknightsMapLayout.Tile { row = row, column = col, kind = kind, surface = cell });
            if (key == 'E' || key == 'G') Portal(cell, key == 'E');
        }
        layout.tiles = tiles.ToArray();
        var route = Group("02 · Enemy route (select map to see gizmos)", root);
        // Cardinal sampling of the source route, including all four MOVE checkpoints.
        int[,] coords = { {2,8}, {2,7}, {2,6}, {2,5}, {3,5}, {3,4}, {3,3}, {2,3}, {2,2}, {2,1}, {3,1}, {3,0} };
        layout.routeWaypoints = new Transform[coords.GetLength(0)];
        for (int i = 0; i < coords.GetLength(0); i++)
        {
            var point = Group(string.Format("{0:00} · R{1} C{2}", i, coords[i,0], coords[i,1]), route);
            point.localPosition = layout.CellToWorld(coords[i,0], coords[i,1]) + Vector3.up * 0.10f;
            layout.routeWaypoints[i] = point;
        }
        environment = Group("03 · Chernobog industrial street", root);
        BuildEnvironment();
        SetupView();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        EditorSceneManager.SaveScene(root.gameObject.scene, "Assets/Scenes/Arknights_0_1.unity");
        Selection.activeGameObject = root.gameObject;
        if (SceneView.lastActiveSceneView)
            SceneView.lastActiveSceneView.LookAt(new Vector3(0, 0.3f, 0.5f), Quaternion.Euler(56, 0, 0), 13f, true, true);
        Debug.Log("Built 0-1 Collapse: 54 cells, 16 highlands, 12 melee tiles, 1 entry and 1 goal. Scene saved.");
    }

    static GameObject SaveTilePrefab(Transform cell, string name)
    {
        var copy = Object.Instantiate(cell.gameObject);
        copy.name = name;
        copy.transform.SetParent(null);
        copy.transform.position = Vector3.zero;
        var prefab = PrefabUtility.SaveAsPrefabAsset(copy, Folder + "/Prefabs/" + name + ".prefab");
        Object.DestroyImmediate(copy);
        return prefab;
    }

    static void CreateMaterials()
    {
        var grain = SurfaceTexture("ConcreteGrain", false);
        var road = SurfaceTexture("RoadGrain", true);
        concrete = Mat("WeatheredConcrete", new Color(0.42f,0.44f,0.47f), 0, grain);
        asphalt = Mat("DarkRoad", new Color(0.38f,0.41f,0.47f), 0, road);
        pale = Mat("HighlandIvory", new Color(0.84f,0.84f,0.81f), 0, grain);
        steel = Mat("StructuralSteel", new Color(0.18f,0.20f,0.21f), 0.5f, grain);
        dark = Mat("DeepRecess", new Color(0.055f,0.065f,0.073f), 0.1f);
        orange = Mat("OxidizedOrange", new Color(0.55f,0.28f,0.08f), 0.2f, grain);
        yellow = Mat("FadedSafetyPaint", new Color(0.79f,0.63f,0.28f), 0);
        paper = Mat("PaperDebris", new Color(0.72f,0.73f,0.68f), 0, grain);
        blue = Mat("BlueGoalLight", new Color(0.11f,0.64f,1f), 0, null, 4.5f);
        red = Mat("RedEntryLight", new Color(0.95f,0.045f,0.065f), 0, null, 3.5f);
    }

    static Texture2D SurfaceTexture(string name, bool road)
    {
        string path = Folder + "/Textures/" + name + ".png";
        const int size = 256;
        var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float coarse = Mathf.PerlinNoise(x * 0.035f, y * 0.035f);
            float fine = (float)random.NextDouble();
            float value = 0.88f + coarse * 0.045f + fine * 0.075f;
            if (road && fine < 0.025f) value *= 0.72f;
            if (!road && (x < 3 || y < 3 || x > 252 || y > 252)) value *= 0.83f;
            pixels[y * size + x] = new Color(value, value, value);
        }
        texture.SetPixels(pixels); texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material Mat(string name, Color color, float metallic, Texture texture = null, float emission = 0)
    {
        string path = Folder + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.16f);
        material.SetFloat("_Metallic", metallic);
        material.SetTexture("_BaseMap", texture);
        if (emission > 0) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * emission); }
        EditorUtility.SetDirty(material);
        return material;
    }

    static Transform Group(string name, Transform parent)
    {
        var t = new GameObject(name).transform; t.SetParent(parent, false); return t;
    }
    static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 size, Material material, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = position; go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material;
        go.isStatic = true;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }
    static void Beam(string name, Transform parent, Vector3 from, Vector3 to, float radius, Material material, bool round = false)
    {
        var go = GameObject.CreatePrimitive(round ? PrimitiveType.Cylinder : PrimitiveType.Cube);
        go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = (from + to) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
        go.transform.localScale = new Vector3(radius, (to - from).magnitude / (round ? 2 : 1), radius);
        go.GetComponent<Renderer>().sharedMaterial = material;
        go.isStatic = true; Object.DestroyImmediate(go.GetComponent<Collider>());
    }
    static void Label(string name, Transform parent, string text, Vector3 position, float size, Color color)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position;
        // Faces the south side of the street, like the building facade.
        go.transform.localRotation = Quaternion.Euler(0, 0, 0);
        var label = go.AddComponent<TextMesh>(); label.text = text; label.fontSize = 64;
        label.characterSize = size; label.anchor = TextAnchor.MiddleCenter; label.color = color;
        go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    static void Portal(Transform cell, bool entry)
    {
        var gate = Group(entry ? "Red enemy entrance" : "Blue defense objective", cell);
        var material = entry ? red : blue;
        const float s = 0.94f, bottom = 0.025f, top = 1.65f;
        foreach (float x in new[] {-s,s})
        foreach (float z in new[] {-s,s})
            Beam("Holographic upright", gate, new Vector3(x,bottom,z), new Vector3(x,top,z), 0.050f, material);
        foreach (float y in new[] {bottom,top})
        {
            Beam("Holographic frame", gate, new Vector3(-s,y,-s), new Vector3(s,y,-s), 0.035f, material);
            Beam("Holographic frame", gate, new Vector3(-s,y,s), new Vector3(s,y,s), 0.035f, material);
            Beam("Holographic frame", gate, new Vector3(-s,y,-s), new Vector3(-s,y,s), 0.035f, material);
            Beam("Holographic frame", gate, new Vector3(s,y,-s), new Vector3(s,y,s), 0.035f, material);
        }
        for (int i=0;i<2;i++)
        {
            float x=0.25f-i*0.6f;
            Beam("Direction chevron",gate,new Vector3(x+0.26f,0.035f,-0.28f),new Vector3(x,0.035f,0),0.045f,material);
            Beam("Direction chevron",gate,new Vector3(x,0.035f,0),new Vector3(x+0.26f,0.035f,0.28f),0.045f,material);
        }
        if (!entry)
        {
            Beam("Rhodes triangle",gate,new Vector3(-0.27f,1.10f,-s),new Vector3(0.27f,1.10f,-s),0.04f,material);
            Beam("Rhodes triangle",gate,new Vector3(-0.27f,1.10f,-s),new Vector3(0,1.58f,-s),0.04f,material);
            Beam("Rhodes triangle",gate,new Vector3(0,1.58f,-s),new Vector3(0.27f,1.10f,-s),0.04f,material);
        }
    }

    static void Crack(Transform cell)
    {
        var point = new Vector3(-0.7f,0.01f,0.35f);
        for (int i=0;i<3;i++)
        {
            var next = point + new Vector3(0.18f+(float)random.NextDouble()*0.14f,0,((float)random.NextDouble()-0.5f)*0.2f);
            Beam("Hairline fracture",cell,point,next,0.012f,dark); point=next;
        }
    }

    static void BuildEnvironment()
    {
        Cube("Street foundation",environment,new Vector3(0,-0.46f,0),new Vector3(25,0.4f,19),dark);
        var north = Group("North factory facade",environment);
        Cube("Factory mass",north,new Vector3(0,2.15f,6.5f),new Vector3(19.8f,4.5f,3),concrete);
        Cube("Lower plinth",north,new Vector3(0,0.42f,4.94f),new Vector3(19.9f,0.75f,0.18f),steel);
        Cube("Rust belt",north,new Vector3(0,0.65f,4.80f),new Vector3(19.9f,0.13f,0.10f),orange);
        foreach(float x in new[]{-9.3f,-5.7f,-1.9f,1.9f,5.7f,9.3f})
        {
            Cube("Facade buttress",north,new Vector3(x,2.4f,4.85f),new Vector3(0.22f,4.6f,0.25f),steel);
            Cube("Buttress foot",north,new Vector3(x,0.63f,4.7f),new Vector3(0.40f,0.9f,0.36f),pale);
        }
        Cube("Service door recess",north,new Vector3(-3.65f,1.55f,4.80f),new Vector3(1.7f,2.4f,0.15f),dark);
        for(int i=0;i<8;i++) Cube("Door steel slats",north,new Vector3(-3.65f,0.52f+i*0.28f,4.69f),new Vector3(1.5f,0.035f,0.065f),steel);
        Cube("Door lamp housing",north,new Vector3(-3.65f,2.86f,4.65f),new Vector3(0.70f,0.19f,0.20f),dark);
        Cube("Door lamp",north,new Vector3(-3.65f,2.85f,4.53f),new Vector3(0.53f,0.11f,0.04f),yellow);
        Cube("Vent recess",north,new Vector3(0,1.1f,4.77f),new Vector3(1.8f,0.78f,0.08f),dark);
        for(int i=0;i<10;i++) Cube("Vent grille",north,new Vector3(-0.79f+i*0.175f,1.1f,4.66f),new Vector3(0.036f,0.65f,0.06f),steel);
        Beam("Vertical drain",north,new Vector3(0.9f,1.7f,4.68f),new Vector3(0.9f,4.6f,4.68f),0.13f,pale,true);
        Beam("Bent drain",north,new Vector3(0.9f,1.7f,4.68f),new Vector3(1.2f,1.5f,4.68f),0.13f,pale,true);
        foreach(float x in new[]{-7.4f,6.2f})
        {
            Cube("Sign mounting",north,new Vector3(x,3.0f,4.65f),new Vector3(4.5f,1.6f,0.2f),dark);
            Cube("Sign orange strip",north,new Vector3(x,3.69f,4.50f),new Vector3(4.3f,0.12f,0.04f),orange);
            Label("Factory sign",north,x<0 ? "CHERNOBOG" : "RESTRICTED",new Vector3(x,3.18f,4.49f),0.10f,new Color(0.73f,0.52f,0.27f));
            Label("Small factory sign",north,x<0 ? "INDUSTRIAL SECTOR / 01" : "AUTHORIZED PERSONNEL",new Vector3(x,2.73f,4.49f),0.052f,new Color(0.59f,0.57f,0.48f));
        }
        // Left scaffold: sparse steel frame and cross-bracing cast the original's long shadows.
        var scaffold=Group("West scaffolding",environment);
        for(int k=0;k<6;k++)
        {
            float z=-4.7f+k*1.9f;
            foreach(float x in new[]{-10.5f,-9.25f})
            {
                Beam("Scaffold upright",scaffold,new Vector3(x,0,z),new Vector3(x,3.45f,z),0.065f,steel,true);
                Cube("Base shoe",scaffold,new Vector3(x,0.04f,z),new Vector3(0.25f,0.08f,0.25f),steel);
            }
            if(k<5)
            {
                foreach(float y in new[]{0.7f,2f,3.3f})
                    Beam("Scaffold rail",scaffold,new Vector3(-9.25f,y,z),new Vector3(-9.25f,y,z+1.9f),0.06f,steel,true);
                Beam("Diagonal scaffold brace",scaffold,new Vector3(-9.25f,0.7f,z),new Vector3(-9.25f,3.3f,z+1.9f),0.045f,steel,true);
                Cube("Scaffold decking",scaffold,new Vector3(-9.88f,2f,z+0.95f),new Vector3(1.3f,0.06f,1.8f),concrete,false);
            }
        }
        Cube("West sidewalk curb",environment,new Vector3(-10.65f,0.05f,0),new Vector3(0.18f,0.12f,13),orange);
        var east=Group("East building and air conditioners",environment);
        Cube("East building",east,new Vector3(11.0f,2.8f,1.9f),new Vector3(3.1f,5.8f,11.2f),concrete);
        Cube("East base ledge",east,new Vector3(9.38f,0.3f,1.9f),new Vector3(0.26f,0.6f,11.3f),steel);
        for(int k=0;k<5;k++)
        {
            float z=-2.6f+k*2.3f;
            Cube("Wall expansion joint",east,new Vector3(9.43f,2.9f,z),new Vector3(0.06f,5.7f,0.045f),dark);
        }
        foreach(float z in new[]{-1.15f,0.5f,3.7f})
        {
            Cube("AC bracket",east,new Vector3(9.14f,1.60f,z),new Vector3(0.60f,0.09f,1.15f),steel);
            Cube("AC unit housing",east,new Vector3(9.06f,1.98f,z),new Vector3(0.66f,0.65f,1.0f),pale);
            Cube("AC grille inset",east,new Vector3(8.713f,1.98f,z),new Vector3(0.022f,0.46f,0.8f),dark,false);
            for(int k=0;k<7;k++) Cube("AC grille slat",east,new Vector3(8.692f,1.79f+k*0.065f,z),new Vector3(0.03f,0.023f,0.76f),steel,false);
            Beam("AC conduit",east,new Vector3(9.43f,1.7f,z+0.65f),new Vector3(9.43f,0.5f,z+0.65f),0.07f,steel,true);
        }
        Beam("East utility pipe",east,new Vector3(9.32f,0.6f,-3.5f),new Vector3(9.32f,4.8f,-3.5f),0.11f,steel,true);
        // Pale broken lane marking below the north deployment band.
        for(int k=0;k<28;k++) Cube("Lane dash",environment,new Vector3(-6.8f+k*0.5f,0.008f,1.84f),new Vector3(0.32f,0.013f,0.045f),yellow,false);
        Cube("Lane border",environment,new Vector3(0,0.008f,1.92f),new Vector3(14,0.013f,0.04f),yellow,false);
        for(int k=0;k<9;k++)
        {
            var slab=Cube("South service paving",environment,new Vector3(-8+k*2,-0.02f,-5.25f),new Vector3(1.9f,0.12f,2.25f),concrete);
            Cube("Inset paving panel",slab.transform,new Vector3(0,0.56f,0),new Vector3(0.84f,0.04f,0.85f),steel,false);
        }
        var machines=Group("South electrical equipment",environment);
        foreach(float x in new[]{3.3f,6.4f})
        {
            Cube("Equipment pedestal",machines,new Vector3(x,0.15f,-6.3f),new Vector3(2.6f,0.36f,2.15f),steel);
            Cube("Electrical cabinet",machines,new Vector3(x,0.87f,-6.25f),new Vector3(2.05f,1.35f,1.7f),concrete);
            Cube("Cabinet lid",machines,new Vector3(x,1.59f,-6.25f),new Vector3(2.2f,0.13f,1.8f),pale);
            for(int k=0;k<8;k++) Cube("Cooling fins",machines,new Vector3(x-0.76f+k*0.21f,0.90f,-7.13f),new Vector3(0.055f,0.78f,0.05f),steel,false);
            foreach(float side in new[]{-1.18f,1.18f}) Beam("Protective rail",machines,new Vector3(x+side,0.4f,-7.1f),new Vector3(x+side,1.75f,-7.1f),0.10f,steel);
        }
        var debris=Group("Street debris and repairs",environment);
        foreach(var p in new[]{new Vector3(3.6f,0.02f,0.3f),new Vector3(4.2f,0.02f,0.5f),new Vector3(-3.4f,0.06f,-5.25f),new Vector3(-2.8f,0.06f,-5.4f)})
        {
            var sheet=Cube("Discarded paper",debris,p,new Vector3(0.22f,0.008f,0.34f),paper,false);
            sheet.transform.localRotation=Quaternion.Euler(0,(float)random.NextDouble()*90,0);
            for(int k=0;k<4;k++) Cube("Paper print",sheet.transform,new Vector3(0,0.65f,-0.27f+k*0.15f),new Vector3(0.68f,0.03f,0.025f),steel,false);
        }
        for(int k=0;k<20;k++)
        {
            float x=-10.6f+(float)random.NextDouble()*1.4f;
            float z=-5f+(float)random.NextDouble()*9;
            var piece=Cube("Concrete rubble",debris,new Vector3(x,0.08f,z),new Vector3(0.08f+(float)random.NextDouble()*0.18f,0.09f,0.12f),pale,false);
            piece.transform.localRotation=Quaternion.Euler(0,(float)random.NextDouble()*180,12);
        }
    }

    static void SetupView()
    {
        var camera = Camera.main;
        if (!camera) { var go=new GameObject("Main Camera"); go.tag="MainCamera"; camera=go.AddComponent<Camera>(); go.AddComponent<AudioListener>(); }
        Undo.RecordObject(camera,"Frame 0-1 map"); Undo.RecordObject(camera.transform,"Frame 0-1 map");
        camera.transform.position=new Vector3(-0.8f,23,-16f);
        camera.transform.LookAt(new Vector3(0,0.2f,0.2f));
        camera.orthographic=false; camera.fieldOfView=29f; camera.orthographicSize=6.9f;
        camera.nearClipPlane=0.1f; camera.farClipPlane=90;
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(0.09f,0.105f,0.12f);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
        var light=GameObject.Find("Directional Light")?.GetComponent<Light>();
        if(!light) light=new GameObject("Directional Light").AddComponent<Light>();
        Undo.RecordObject(light,"Light 0-1 map"); Undo.RecordObject(light.transform,"Light 0-1 map");
        light.type=LightType.Directional; light.transform.rotation=Quaternion.Euler(44,-37,0);
        light.color=new Color(1f,0.94f,0.84f); light.intensity=2.8f;
        light.shadows=LightShadows.Soft; light.shadowStrength=0.88f;
        light.shadowBias=0.025f; light.shadowNormalBias=0.12f;
        RenderSettings.skybox=null; RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(0.52f,0.57f,0.78f);
        RenderSettings.ambientEquatorColor=new Color(0.40f,0.42f,0.56f);
        RenderSettings.ambientGroundColor=new Color(0.24f,0.25f,0.30f);
        RenderSettings.ambientIntensity=1f; RenderSettings.fog=false; RenderSettings.sun=light;
        var volumeGo=GameObject.Find("Global Volume");
        if(!volumeGo) volumeGo=new GameObject("Global Volume");
        var volume=volumeGo.GetComponent<Volume>(); if(!volume) volume=volumeGo.AddComponent<Volume>();
        string path=Folder+"/Materials/StreetPostProcessing.asset";
        var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if(!profile) { profile=ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile,path); }
        if(!profile.TryGet<Bloom>(out var bloom)) { bloom=profile.Add<Bloom>(true); AssetDatabase.AddObjectToAsset(bloom,profile); }
        bloom.intensity.Override(0.18f); bloom.threshold.Override(1.25f); bloom.scatter.Override(0.45f);
        if(!profile.TryGet<ColorAdjustments>(out var color)) { color=profile.Add<ColorAdjustments>(true); AssetDatabase.AddObjectToAsset(color,profile); }
        color.contrast.Override(8); color.saturation.Override(-8); color.postExposure.Override(0.25f);
        if(!profile.TryGet<Vignette>(out var vignette)) { vignette=profile.Add<Vignette>(true); AssetDatabase.AddObjectToAsset(vignette,profile); }
        vignette.intensity.Override(0.17f); vignette.smoothness.Override(0.65f);
        volume.isGlobal=true; volume.sharedProfile=profile;
        EditorUtility.SetDirty(profile); EditorUtility.SetDirty(volume);
    }
}
