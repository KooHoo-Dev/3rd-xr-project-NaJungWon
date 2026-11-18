#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TimeSlasher
{
    // JTS The Samurai Sword 머티리얼을 URP Lit으로 바꾸는 에디터 도구
    // - 메뉴: Tools → Time Slasher → Convert Katana Materials to URP
    //
    // 왜 필요한가
    // - 원래 셰이더 JTS/CustomSword는 Built-in 전용(Surface Shader)이라 URP에서 분홍색으로 나옴
    // - 에셋에 들어 있는 URP 패키지(_SRP 폴더)는 2019년 Lightweight용 Shader Graph라 Unity 6에서 보장 못 함
    // → 가장 확실하고 Quest에서도 가벼운 URP Lit으로 바꿈 (천 손잡이의 비단 반짝임 효과만 빠짐)
    //
    // 원래 텍스처를 URP Lit에 옮기는 방법
    // - _MainTex(색)          → _BaseMap
    // - _Color (a = 섞는 정도) → _BaseColor
    // - _NormTex(노멀)        → _BumpMap   (텍스처 타입을 Normal map으로 바꿔야 URP가 제대로 읽음)
    // - _MaskTex(R 금속, G 매끈함) → URP는 "R 금속, A 매끈함"을 원하므로 새 텍스처(_urp_mg.png)를 만들어 넣음
    public static class KatanaUrpConverter
    {
        private const string MaterialFolder = "Assets/99. External Assets/JTS_TheSamuraiSword/Materials";
        private const string OldShaderName = "JTS/CustomSword";

        [MenuItem("Tools/Time Slasher/Convert Katana Materials to URP")]
        public static void ConvertAll()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogWarning("[KatanaUrpConverter] URP Lit 셰이더를 찾지 못했습니다.");
                return;
            }

            // 같은 마스크를 여러 머티리얼이 쓰므로 한 번 만든 텍스처는 다시 씀
            Dictionary<Texture, Texture2D> madeMaps = new Dictionary<Texture, Texture2D>();
            int count = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null || mat.shader.name != OldShaderName) continue;

                ConvertOne(mat, lit, madeMaps);
                count++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[KatanaUrpConverter] 머티리얼 {count}개를 URP Lit으로 바꿨습니다.");
        }

        private static void ConvertOne(Material mat, Shader lit, Dictionary<Texture, Texture2D> madeMaps)
        {
            // 1. 셰이더를 바꾸기 전에 원래 값을 꺼내 둠
            Texture mainTex = mat.GetTexture("_MainTex");
            Texture normTex = mat.GetTexture("_NormTex");
            Texture maskTex = mat.GetTexture("_MaskTex");
            Color tint = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;

            Undo.RecordObject(mat, "Convert Katana Material");
            mat.shader = lit;

            // 2. 색: 원래 셰이더는 "텍스처 색과 (텍스처 색 × _Color)를 _Color.a 만큼 섞음"
            mat.SetTexture("_BaseMap", mainTex);
            Color baseColor = Color.Lerp(Color.white, tint, tint.a);
            baseColor.a = 1f;
            mat.SetColor("_BaseColor", baseColor);

            // 3. 노멀
            if (normTex != null)
            {
                MakeNormalMap(normTex);
                mat.SetTexture("_BumpMap", normTex);
                mat.SetFloat("_BumpScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
            }

            // 4. 금속·매끈함
            if (maskTex != null)
            {
                if (madeMaps.TryGetValue(maskTex, out Texture2D mg) == false)
                {
                    mg = MakeMetallicGlossMap(maskTex);
                    madeMaps[maskTex] = mg;
                }
                mat.SetTexture("_MetallicGlossMap", mg);
                mat.SetFloat("_Smoothness", 1f);                // 매끈함은 텍스처 값을 그대로 씀
                mat.SetFloat("_SmoothnessTextureChannel", 0f);  // 매끈함을 금속 텍스처의 알파(A)에서 읽음
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            else
            {
                mat.SetTexture("_MetallicGlossMap", null);
                mat.SetFloat("_Metallic", 0.8f);
                mat.SetFloat("_Smoothness", 0.7f);
                mat.DisableKeyword("_METALLICSPECGLOSSMAP");
            }

            EditorUtility.SetDirty(mat);
        }

        // 노멀 텍스처의 타입을 Normal map으로 바꿈 (원본은 "Advanced"라 URP가 노멀로 못 읽음)
        private static void MakeNormalMap(Texture tex)
        {
            string path = AssetDatabase.GetAssetPath(tex);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.textureType == TextureImporterType.NormalMap) return;

            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }

        // [수학] AI 작성
        // 마스크(R 금속, G 매끈함)를 URP용(R 금속, A 매끈함) 텍스처로 새로 만들어 저장
        // 원본이 읽기 불가 설정이어도 되도록, RenderTexture에 그려서 픽셀을 읽음
        private static Texture2D MakeMetallicGlossMap(Texture mask)
        {
            string maskPath = AssetDatabase.GetAssetPath(mask);
            string outPath = Path.Combine(Path.GetDirectoryName(maskPath),
                Path.GetFileNameWithoutExtension(maskPath) + "_urp_mg.png").Replace('\\', '/');

            // 원본이 sRGB로 불러와졌으면 sRGB로 그려야 원래 값이 그대로 나옴
            TextureImporter maskImporter = AssetImporter.GetAtPath(maskPath) as TextureImporter;
            bool srgb = maskImporter != null && maskImporter.sRGBTexture;

            int w = mask.width;
            int h = mask.height;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32,
                srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            Graphics.Blit(mask, rt);

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D read = new Texture2D(w, h, TextureFormat.RGBA32, false, srgb == false);
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            // R = 금속(그대로), G·B = 안 씀, A = 매끈함(원래 G)
            Color32[] pixels = read.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                byte metal = pixels[i].r;
                byte smooth = pixels[i].g;
                pixels[i] = new Color32(metal, metal, metal, smooth);
            }
            read.SetPixels32(pixels);
            read.Apply();

            File.WriteAllBytes(outPath, read.EncodeToPNG());
            Object.DestroyImmediate(read);

            // 금속 텍스처는 색이 아니라 데이터이므로 sRGB 끔
            AssetDatabase.ImportAsset(outPath);
            TextureImporter outImporter = AssetImporter.GetAtPath(outPath) as TextureImporter;
            if (outImporter != null)
            {
                outImporter.sRGBTexture = false;
                outImporter.alphaSource = TextureImporterAlphaSource.FromInput;
                outImporter.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }
    }
}
#endif
