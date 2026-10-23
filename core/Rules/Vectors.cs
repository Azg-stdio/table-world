using System;
using Newtonsoft.Json;

namespace Core.Rules
{
    /// <summary>Three floats serialized as a JSON array <c>[x, y, z]</c>. Meters or Euler degrees depending on use.</summary>
    [JsonConverter(typeof(Vec3Converter))]
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);
        public static readonly Vec3 One = new Vec3(1f, 1f, 1f);

        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(Vec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object? obj) => obj is Vec3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public static bool operator ==(Vec3 a, Vec3 b) => a.Equals(b);
        public static bool operator !=(Vec3 a, Vec3 b) => !a.Equals(b);
        public override string ToString() => $"[{X}, {Y}, {Z}]";
    }

    /// <summary>Two floats serialized as a JSON array <c>[x, y]</c>. Used for sizes in meters.</summary>
    [JsonConverter(typeof(Vec2Converter))]
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly float X;
        public readonly float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object? obj) => obj is Vec2 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public static bool operator ==(Vec2 a, Vec2 b) => a.Equals(b);
        public static bool operator !=(Vec2 a, Vec2 b) => !a.Equals(b);
        public override string ToString() => $"[{X}, {Y}]";
    }

    internal sealed class Vec3Converter : JsonConverter<Vec3>
    {
        public override Vec3 ReadJson(JsonReader reader, Type objectType, Vec3 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var values = FloatArray.Read(reader, 3);
            return new Vec3(values[0], values[1], values[2]);
        }

        public override void WriteJson(JsonWriter writer, Vec3 value, JsonSerializer serializer)
        {
            writer.WriteStartArray();
            writer.WriteValue(value.X);
            writer.WriteValue(value.Y);
            writer.WriteValue(value.Z);
            writer.WriteEndArray();
        }
    }

    internal sealed class Vec2Converter : JsonConverter<Vec2>
    {
        public override Vec2 ReadJson(JsonReader reader, Type objectType, Vec2 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var values = FloatArray.Read(reader, 2);
            return new Vec2(values[0], values[1]);
        }

        public override void WriteJson(JsonWriter writer, Vec2 value, JsonSerializer serializer)
        {
            writer.WriteStartArray();
            writer.WriteValue(value.X);
            writer.WriteValue(value.Y);
            writer.WriteEndArray();
        }
    }

    internal static class FloatArray
    {
        public static float[] Read(JsonReader reader, int length)
        {
            if (reader.TokenType != JsonToken.StartArray)
                throw new JsonSerializationException($"Expected an array of {length} numbers.");

            var values = new float[length];
            var count = 0;
            while (reader.Read() && reader.TokenType != JsonToken.EndArray)
            {
                if (reader.TokenType != JsonToken.Integer && reader.TokenType != JsonToken.Float)
                    throw new JsonSerializationException($"Expected an array of {length} numbers.");
                if (count < length)
                    values[count] = Convert.ToSingle(reader.Value, System.Globalization.CultureInfo.InvariantCulture);
                count++;
            }

            if (count != length)
                throw new JsonSerializationException($"Expected an array of {length} numbers, got {count}.");
            return values;
        }
    }
}
