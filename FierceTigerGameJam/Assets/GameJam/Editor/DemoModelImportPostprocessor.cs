#if UNITY_EDITOR
using UnityEditor;

namespace GameJam.EditorTools
{
    public sealed class DemoModelImportPostprocessor : AssetPostprocessor
    {
        private const string DemoModelPath = "Assets/GameJam/FBX/ModelDemo.fbx";

        private void OnPreprocessModel()
        {
            if (assetPath != DemoModelPath)
            {
                return;
            }

            ModelImporter importer = (ModelImporter)assetImporter;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.addCollider = false;
            importer.isReadable = true;
            importer.preserveHierarchy = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }
    }
}
#endif
