using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if UNITY_EDITOR
public class MaterialURPConverter : EditorWindow
{
    private Vector2 scrollPosition;
    private List<Renderer> problemRenderers = new List<Renderer>();
    private List<Material> problemMaterials = new List<Material>();
    private bool includeInactive = true;
    private bool includePrefabs = true;
    private bool convertAllBuiltInMaterials = false;

    [MenuItem("Tools/URP/Material Converter")]
    public static void ShowWindow()
    {
        GetWindow<MaterialURPConverter>("URP Material Converter");
    }

    void OnGUI()
    {
        GUILayout.Label("Find and Fix Pink Materials in URP", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        includeInactive = EditorGUILayout.Toggle("Include Inactive Objects", includeInactive);
        includePrefabs = EditorGUILayout.Toggle("Include Prefabs", includePrefabs);
        convertAllBuiltInMaterials = EditorGUILayout.Toggle("Convert ALL Built-in Materials", convertAllBuiltInMaterials);

        EditorGUILayout.HelpBox("When 'Convert ALL Built-in Materials' is enabled, ALL materials using Built-in shaders will be converted to URP equivalents, not just pink ones.", MessageType.Info);

        EditorGUILayout.Space();

        if (GUILayout.Button("Scan Scene for Pink Materials"))
        {
            FindProblemMaterials();
        }

        if (GUILayout.Button("Convert All Materials to URP"))
        {
            ConvertAllMaterials();
        }

        if (GUILayout.Button("Fix Selected Objects Only"))
        {
            FixSelectedObjects();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Found {problemRenderers.Count} renderers with pink materials", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Found {problemMaterials.Count} unique pink materials", EditorStyles.boldLabel);

        if (problemRenderers.Count > 0)
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(300));

            foreach (var renderer in problemRenderers)
            {
                if (renderer == null) continue;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(renderer.gameObject, typeof(GameObject), true);

                if (GUILayout.Button("Select", GUILayout.Width(60)))
                {
                    Selection.activeGameObject = renderer.gameObject;
                }

                if (GUILayout.Button("Fix", GUILayout.Width(40)))
                {
                    FixRendererMaterials(renderer);
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }
    }

    private void FindProblemMaterials()
    {
        problemRenderers.Clear();
        problemMaterials.Clear();

        var allRenderers = FindObjectsOfType<Renderer>(includeInactive);

        foreach (var renderer in allRenderers)
        {
            // Проверяем, является ли объект префабом (если нужно)
            if (!includePrefabs && PrefabUtility.IsPartOfAnyPrefab(renderer))
                continue;

            var materials = renderer.sharedMaterials;
            bool hasProblem = false;

            foreach (var mat in materials)
            {
                if (mat == null) continue;

                // Проверка на "розовый" материал (отсутствующий шейдер)
                if (mat.shader.name.Contains("Error") ||
                    mat.shader.name == "Hidden/InternalErrorShader" ||
                    IsMaterialPink(mat))
                {
                    if (!problemMaterials.Contains(mat))
                        problemMaterials.Add(mat);
                    hasProblem = true;
                }
            }

            if (hasProblem)
                problemRenderers.Add(renderer);
        }

        Debug.Log($"Found {problemRenderers.Count} renderers with pink materials");
        Debug.Log($"Found {problemMaterials.Count} unique pink materials");
    }

    private bool IsMaterialPink(Material material)
    {
        // Дополнительная проверка: материал розового цвета часто имеет стандартный шейдер
        // или шейдер из built-in pipeline
        string shaderName = material.shader.name.ToLower();

        // Шейдеры built-in pipeline которые могут вызывать проблемы в URP
        string[] builtInShaderKeywords = {
            "standard",
            "legacy",
            "built-in",
            "particle",
            "unlit",
            "mobile",
            "terrain"
        };

        foreach (var keyword in builtInShaderKeywords)
        {
            if (shaderName.Contains(keyword))
                return true;
        }

        return false;
    }

    private void ConvertAllMaterials()
    {
        // Сначала очищаем списки
        problemRenderers.Clear();
        problemMaterials.Clear();

        // Находим ВСЕ рендереры на сцене
        var allRenderers = FindObjectsOfType<Renderer>(includeInactive);

        Debug.Log($"Scanning {allRenderers.Length} renderers for conversion...");

        int fixedCount = 0;
        int processedCount = 0;

        // Используем ProgressBar для отображения прогресса
        try
        {
            foreach (var renderer in allRenderers)
            {
                processedCount++;

                // Обновляем прогресс бар
                if (EditorUtility.DisplayCancelableProgressBar(
                    "Converting Materials to URP",
                    $"Processing {renderer.name}... ({processedCount}/{allRenderers.Length})",
                    (float)processedCount / allRenderers.Length))
                {
                    Debug.Log("Conversion cancelled by user");
                    break;
                }

                // Проверяем, является ли объект префабом (если нужно)
                if (!includePrefabs && PrefabUtility.IsPartOfAnyPrefab(renderer))
                    continue;

                // Конвертируем материалы рендерера
                if (FixRendererMaterials(renderer))
                {
                    fixedCount++;
                    problemRenderers.Add(renderer); // Добавляем в список для отображения
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Debug.Log($"Fixed {fixedCount} renderers out of {processedCount} processed");

        // После конвертации обновляем список проблемных материалов
        if (fixedCount > 0)
        {
            // Сохраняем все изменения
            AssetDatabase.SaveAssets();
            FindProblemMaterials(); // Обновляем список
        }
    }

    private void FixSelectedObjects()
    {
        int fixedCount = 0;
        int totalProcessed = 0;

        foreach (var obj in Selection.gameObjects)
        {
            var renderers = obj.GetComponentsInChildren<Renderer>(includeInactive);

            foreach (var renderer in renderers)
            {
                totalProcessed++;
                if (FixRendererMaterials(renderer))
                    fixedCount++;
            }
        }
        Debug.Log($"Fixed {fixedCount} renderers out of {totalProcessed} in selected objects");
    }

    private bool FixRendererMaterials(Renderer renderer)
    {
        if (renderer == null) return false;

        bool wasFixed = false;
        var materials = renderer.sharedMaterials;

        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i] == null) continue;

            // Проверяем, нужно ли конвертировать этот материал
            if (ShouldConvertMaterial(materials[i]))
            {
                Material newMaterial = ConvertMaterialToURP(materials[i]);
                if (newMaterial != materials[i])
                {
                    materials[i] = newMaterial;
                    wasFixed = true;
                }
            }
        }

        if (wasFixed)
        {
            renderer.sharedMaterials = materials;
            EditorUtility.SetDirty(renderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }

        return wasFixed;
    }

    private bool ShouldConvertMaterial(Material material)
    {
        if (material == null) return false;

        string shaderName = material.shader.name;

        // Всегда конвертируем розовые материалы
        if (shaderName.Contains("Error") || shaderName == "Hidden/InternalErrorShader")
            return true;

        // Если включена опция "Convert ALL Built-in Materials", конвертируем все Built-in шейдеры
        if (convertAllBuiltInMaterials)
        {
            return IsBuiltInShader(material);
        }

        // Иначе только розовые и явно проблемные
        return IsMaterialPink(material);
    }

    private bool IsBuiltInShader(Material material)
    {
        string shaderName = material.shader.name.ToLower();

        // Список шейдеров Built-in pipeline
        string[] builtInShaderPatterns = {
            "standard",
            "specular",
            "diffuse",
            "bumped",
            "parallax",
            "legacy shaders/",
            "mobile/",
            "nature/",
            "particles/",
            "terrain/",
            "tree",
            "unlit",
            "vertexlit",
            "reflective",
            "toon",
            "rim",
            "outline"
        };

        foreach (var pattern in builtInShaderPatterns)
        {
            if (shaderName.Contains(pattern))
                return true;
        }

        return false;
    }

    private Material ConvertMaterialToURP(Material originalMaterial)
    {
        if (originalMaterial == null) return originalMaterial;

        string shaderName = originalMaterial.shader.name;
        string originalShaderName = shaderName; // Сохраняем оригинальное имя для логов
        Shader urpShader = null;

        // Маппинг шейдеров из Built-in в URP
        if (shaderName.Contains("Standard") || shaderName.Contains("Specular"))
        {
            urpShader = Shader.Find("Universal Render Pipeline/Lit");
        }
        else if (shaderName.Contains("Unlit"))
        {
            urpShader = Shader.Find("Universal Render Pipeline/Unlit");
        }
        else if (shaderName.Contains("Particle"))
        {
            if (shaderName.Contains("Additive") || shaderName.Contains("Blend"))
                urpShader = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
            else
                urpShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        }
        else if (shaderName.Contains("Terrain"))
        {
            urpShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
        }
        else if (shaderName.Contains("UI/Default") || shaderName.Contains("UI/"))
        {
            urpShader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
        }
        else if (shaderName.Contains("Diffuse") || shaderName.Contains("Bumped Diffuse"))
        {
            urpShader = Shader.Find("Universal Render Pipeline/Lit");
        }
        else if (shaderName.Contains("Nature/Terrain"))
        {
            urpShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
        }
        else if (shaderName.Contains("Sprites/Default"))
        {
            urpShader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
        }
        else
        {
            // По умолчанию используем Lit шейдер
            urpShader = Shader.Find("Universal Render Pipeline/Lit");
        }

        if (urpShader == null)
        {
            Debug.LogWarning($"Could not find URP shader for: {shaderName}. Using default URP Lit shader.");
            urpShader = Shader.Find("Universal Render Pipeline/Lit");

            if (urpShader == null)
            {
                Debug.LogError("Cannot find any URP shaders! Make sure URP is installed in your project.");
                return originalMaterial;
            }
        }

        // Создаем новый материал с URP шейдером
        Material newMaterial = new Material(urpShader);

        // Генерируем уникальное имя для материала
        string baseName = originalMaterial.name.Replace("(Instance)", "").Trim();
        newMaterial.name = $"{baseName}_URP_{System.Guid.NewGuid().ToString().Substring(0, 8)}";

        // Копируем основные свойства
        CopyMaterialProperties(originalMaterial, newMaterial, shaderName);

        // Сохраняем новый материал в папке проекта
        string path = "Assets/URP_Converted_Materials/";
        if (!System.IO.Directory.Exists(path))
            System.IO.Directory.CreateDirectory(path);

        string fullPath = AssetDatabase.GenerateUniqueAssetPath(path + newMaterial.name + ".mat");
        AssetDatabase.CreateAsset(newMaterial, fullPath);

        Debug.Log($"Converted material: {originalShaderName} -> {urpShader.name}. Saved to: {fullPath}");

        return newMaterial;
    }

    private void CopyMaterialProperties(Material source, Material destination, string originalShaderName)
    {
        // Цвет
        if (source.HasProperty("_Color") && destination.HasProperty("_BaseColor"))
            destination.SetColor("_BaseColor", source.color);
        else if (source.HasProperty("_Color"))
            destination.color = source.color;

        // Основная текстура
        if (source.HasProperty("_MainTex"))
        {
            Texture mainTex = source.mainTexture;
            if (mainTex != null)
            {
                if (destination.HasProperty("_BaseMap"))
                    destination.SetTexture("_BaseMap", mainTex);
                else if (destination.HasProperty("_MainTex"))
                    destination.SetTexture("_MainTex", mainTex);
            }

            // Tiling и Offset
            if (source.HasProperty("_MainTex_ST"))
            {
                Vector2 scale = source.mainTextureScale;
                Vector2 offset = source.mainTextureOffset;

                if (destination.HasProperty("_BaseMap"))
                {
                    destination.SetTextureScale("_BaseMap", scale);
                    destination.SetTextureOffset("_BaseMap", offset);
                }
                else if (destination.HasProperty("_MainTex"))
                {
                    destination.SetTextureScale("_MainTex", scale);
                    destination.SetTextureOffset("_MainTex", offset);
                }
            }
        }

        // Нормальная карта
        if (source.HasProperty("_BumpMap") && destination.HasProperty("_BumpMap"))
        {
            Texture bumpMap = source.GetTexture("_BumpMap");
            if (bumpMap != null)
                destination.SetTexture("_BumpMap", bumpMap);
        }

        // Металлик и гладкость
        if (source.HasProperty("_Metallic") && destination.HasProperty("_Metallic"))
            destination.SetFloat("_Metallic", source.GetFloat("_Metallic"));

        if (source.HasProperty("_Glossiness") && destination.HasProperty("_Smoothness"))
            destination.SetFloat("_Smoothness", source.GetFloat("_Glossiness"));
        else if (source.HasProperty("_Smoothness") && destination.HasProperty("_Smoothness"))
            destination.SetFloat("_Smoothness", source.GetFloat("_Smoothness"));

        // Эмиссия
        if (source.HasProperty("_EmissionColor") && destination.HasProperty("_EmissionColor"))
            destination.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));

        if (source.IsKeywordEnabled("_EMISSION") && destination.HasProperty("_EmissionColor"))
            destination.EnableKeyword("_EMISSION");

        // Прочие свойства
        if (source.HasProperty("_Cutoff") && destination.HasProperty("_Cutoff"))
            destination.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));

        // Прозрачность
        if (source.HasProperty("_Mode"))
        {
            float mode = source.GetFloat("_Mode");
            if (destination.HasProperty("_Surface"))
            {
                // 0 = Opaque, 1 = Cutout, 2 = Fade, 3 = Transparent
                if (mode == 0) // Opaque
                    destination.SetFloat("_Surface", 0);
                else if (mode == 1) // Cutout
                {
                    destination.SetFloat("_Surface", 1);
                    destination.SetFloat("_AlphaClip", 1);
                }
                else // Fade or Transparent
                {
                    destination.SetFloat("_Surface", 1);
                    destination.SetFloat("_Blend", 1); // Alpha blend
                    destination.SetFloat("_AlphaClip", 0);
                }
            }
        }
    }

    // Альтернативный метод для быстрого исправления через контекстное меню
    [MenuItem("GameObject/URP/Fix Pink Material", false, 0)]
    private static void FixPinkMaterialContextMenu()
    {
        var converter = CreateInstance<MaterialURPConverter>();
        foreach (var obj in Selection.gameObjects)
        {
            var renderers = obj.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                converter.FixRendererMaterials(renderer);
            }
        }
        DestroyImmediate(converter);
    }

    [MenuItem("GameObject/URP/Fix Pink Material", true)]
    private static bool ValidateFixPinkMaterialContextMenu()
    {
        return Selection.activeGameObject != null;
    }
}
#endif