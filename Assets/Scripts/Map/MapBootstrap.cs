using UnityEngine;
using StarBound.Core;

namespace StarBound.Map
{
    // Auto-generates and renders a preview map on scene load, with no
    // manual scene setup required. Difficulty/size selection via UI is a
    // separate later story — this exists purely to make map generation
    // and rendering visually testable end to end.
    public static class MapBootstrap
    {
        private const MapSize DefaultMapSize = MapSize.Small;
        private const Difficulty DefaultDifficulty = Difficulty.Medium;
        private const float DefaultHexRadius = 1f;
        private const float CameraPadding = 1.8f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreatePreviewMap()
        {
            var radius = DefaultMapSize.ToRadius();
            var seed = System.Environment.TickCount;

            var host = new GameObject("MapView (auto-generated preview)", typeof(MapView));
            host.GetComponent<MapView>().Generate(radius, DefaultDifficulty, seed, DefaultHexRadius);

            FitCameraToMap(radius, DefaultHexRadius);
        }

        private static void FitCameraToMap(int radius, float hexRadius)
        {
            var camera = Camera.main;
            if (camera == null || !camera.orthographic)
                return;

            var worldRadius = radius * hexRadius * CameraPadding;
            camera.orthographicSize = Mathf.Max(worldRadius, 2f);

            var position = camera.transform.position;
            camera.transform.position = new Vector3(0f, 0f, position.z);
        }
    }
}
