using System;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace DynamicMaps.Config;

/// <summary>
/// Replacement for the BepInEx 5 shortcut type, which is not part of BepInEx 6.
/// </summary>
public readonly struct KeyboardShortcut
{
    public KeyCode MainKey { get; }
    public KeyCode[] Modifiers { get; }

    public KeyboardShortcut(KeyCode mainKey, params KeyCode[] modifiers)
    {
        MainKey = mainKey;
        Modifiers = modifiers ?? Array.Empty<KeyCode>();
    }

    public static void RegisterConfigConverter()
    {
        TomlTypeConverter.AddConverter(typeof(KeyboardShortcut), new TypeConverter
        {
            ConvertToString = (value, _) =>
            {
                var shortcut = (KeyboardShortcut)value;
                return string.Join(" + ", shortcut.Modifiers.Append(shortcut.MainKey));
            },
            ConvertToObject = (value, _) =>
            {
                var keys = value.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(key => Enum.Parse<KeyCode>(key.Trim(), ignoreCase: true))
                    .ToArray();
                if (keys.Length == 0)
                    throw new FormatException("A shortcut must contain at least one key.");

                return new KeyboardShortcut(keys[^1], keys.Take(keys.Length - 1).ToArray());
            }
        });
    }
}
