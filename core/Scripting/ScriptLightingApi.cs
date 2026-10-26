using System;
using System.Collections.Generic;
using Core.Engine;
using Core.Rules;
using MoonSharp.Interpreter;

namespace Core.Scripting
{
    /// <summary>
    /// TTS's <c>Lighting</c>: a script sets <c>light_intensity</c>, <c>ambient_intensity</c> or <c>light_color</c> and
    /// calls <c>apply()</c> (Pokémon Sinnoh dims the room as its distortion world opens). The values become multipliers
    /// of our room's light (<see cref="LightingDef"/>); TTS's other fields (ambient type and colours, LUTs, reflections)
    /// are kept on the table so scripts read back what they set, but do not change our room.
    /// </summary>
    public sealed partial class ScriptHost
    {
        private Table LightingApi(Script script)
        {
            var lighting = new Table(script);
            var now = Engine.State.Environment?.Lighting ?? new LightingDef();
            lighting["light_intensity"] = TtsLighting.DefaultLightIntensity * (now.LightScale ?? 1f);
            lighting["ambient_intensity"] = TtsLighting.DefaultAmbientIntensity * (now.AmbientScale ?? 1f);
            lighting["ambient_type"] = 1;
            lighting["reflection_intensity"] = 1;
            lighting["lut_index"] = 0;
            lighting["lut_contribution"] = 1;
            lighting["lut_url"] = "";
            var tint = now.LightTint ?? new[] { 1f, 1f, 1f };
            lighting["light_color"] = ColorTable(script,
                TtsLighting.DefaultLightColor[0] * tint[0], TtsLighting.DefaultLightColor[1] * tint[1], TtsLighting.DefaultLightColor[2] * tint[2]);

            int Self(CallbackArguments a) => a.Count > 0 && a[0].Type == DataType.Table && a[0].Table == lighting ? 1 : 0;
            DynValue Arg(CallbackArguments a) => a.Count > Self(a) ? a[Self(a)] : DynValue.Nil;
            lighting["getLightColor"] = DynValue.NewCallback((_, a) => lighting.Get("light_color"));
            lighting["setLightColor"] = DynValue.NewCallback((_, a) =>
            {
                if (Arg(a).Type != DataType.Table)
                    return DynValue.False;
                lighting["light_color"] = Arg(a);
                return DynValue.True;
            });
            foreach (var name in new[] { "AmbientSkyColor", "AmbientEquatorColor", "AmbientGroundColor" })
            {
                var key = "ambient_" + name.Substring(7, name.Length - 12).ToLowerInvariant() + "_color";
                lighting[key] = ColorTable(script, 1, 1, 1);
                lighting["get" + name] = DynValue.NewCallback((_, a) => lighting.Get(key));
                lighting["set" + name] = DynValue.NewCallback((_, a) =>
                {
                    if (Arg(a).Type == DataType.Table)
                        lighting[key] = Arg(a);
                    return DynValue.True;
                });
            }
            lighting["apply"] = DynValue.NewCallback((_, a) =>
            {
                float? Scale(string key, float normal)
                {
                    var value = lighting.Get(key);
                    if (value.Type != DataType.Number)
                        return null;
                    var scale = (float)Math.Max(0, Math.Min(8, value.Number / normal));
                    return Math.Abs(scale - 1f) < 0.005f ? (float?)null : scale;
                }
                float[]? Tint()
                {
                    if (lighting.Get("light_color").Type != DataType.Table)
                        return null;
                    var c = lighting.Get("light_color").Table;
                    float Channel(string key, int index) =>
                        (float)(c.Get(key).Type == DataType.Number ? c.Get(key).Number : c.Get(index).Type == DataType.Number ? c.Get(index).Number : 1);
                    return TtsLighting.Tint(Channel("r", 1), Channel("g", 2), Channel("b", 3));
                }
                return DynValue.NewBoolean(Issue(new SetLightingCommand
                {
                    LightScale = Scale("light_intensity", TtsLighting.DefaultLightIntensity),
                    AmbientScale = Scale("ambient_intensity", TtsLighting.DefaultAmbientIntensity),
                    LightTint = Tint(),
                }));
            });
            return lighting;
        }

        /// <summary>A TTS colour (<c>{ r, g, b, a }</c>, also <c>c[1]</c> …): the prelude's Color when it is there.</summary>
        private static DynValue ColorTable(Script script, double r, double g, double b)
        {
            var color = script.Globals.Get("Color");
            if (color.Type == DataType.Table || color.Type == DataType.Function)
            {
                try
                {
                    if (script.Call(color, r, g, b) is { Type: DataType.Table } made)
                        return made;
                }
                catch (InterpreterException)
                {
                }
            }
            var t = new Table(script);
            t["r"] = r;
            t["g"] = g;
            t["b"] = b;
            t["a"] = 1;
            t[1] = r;
            t[2] = g;
            t[3] = b;
            t[4] = 1;
            return DynValue.NewTable(t);
        }
    }
}
