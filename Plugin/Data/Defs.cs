using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

namespace DynamicMaps.Data
{
    public class BoundingRectangle
    {
        public Vector2 Min { get; set; }
        public Vector2 Max { get; set; }
    }

    public class BoundingRectangularSolid
    {
        public Vector3 Min { get; set; }
        public Vector3 Max { get; set; }
    }

    public class MapLayerDef
    {
        public int Level { get; set; }
        public string ImagePath { get; set; }
        public int TesselationIndex { get; set; }
        public BoundingRectangle ImageBounds { get; set; }
        public List<BoundingRectangularSolid> GameBounds { get; set; }
    }

    public class MapMarkerDef
    {
        public string Text { get; set; } = "";
        public string ImagePath { get; set; }
        public Vector3 Position { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public Sprite Sprite { get; set; }
        public bool ShowInRaid { get; set; } = true;
        public string Category { get; set; } = "None";
        public Color Color { get; set; } = Color.white;
        public Vector2 Pivot { get; set; } = new Vector2(0.5f, 0.5f);
        public string AssociatedItemId { get; set; } = "";
    }

    public class MapLabelDef
    {
        public string Text { get; set; }
        public Vector3 Position { get; set; }

        public float FontSize { get; set; } = 14f;
        public float DegreesRotation { get; set; } = 0f;
        public Color Color { get; set; } = Color.white;
        public string Category { get; set; } = "None";
    }

    public class MapDef
    {
        public string DisplayName { get; set; }
        public BoundingRectangle Bounds { get; set; }
        public Dictionary<string, MapLayerDef> Layers { get; set; } = new Dictionary<string, MapLayerDef>();

        public List<string> MapInternalNames { get; set; } = new List<string>();
        public float CoordinateRotation { get; set; } = 0;
        public List<MapLabelDef> Labels { get; set; } = new List<MapLabelDef>();
        public List<MapMarkerDef> StaticMarkers { get; set; } = new List<MapMarkerDef>();
        public int DefaultLevel { get; set; } = 0;

        public string Author { get; set; }
        public string AuthorLink { get; set; }

        public static MapDef LoadFromPath(string absolutePath)
        {
            try
            {
                // EFT ships JsonConvert as an IL2CPP proxy; its generated generic overload
                // cannot deserialize managed plugin DTOs. Use the managed .NET serializer.
                var options = new JsonSerializerOptions
                {
                    AllowTrailingCommas = true,
                    IncludeFields = true,
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                return JsonSerializer.Deserialize(File.ReadAllText(absolutePath), typeof(MapDef), options) as MapDef;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Loading MapMappingDef failed from json at path: {absolutePath}");
                Plugin.Log.LogError($"Exception given was: {e}");
            }

            return null;
        }
    }
}
