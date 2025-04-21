// FamilyBudgetApi/Converters/TimestampJsonConverter.cs
using Google.Cloud.Firestore;
using Google.Protobuf.WellKnownTypes;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyBudgetApi.Converters
{
    public class TimestampJsonConverter : JsonConverter<Google.Cloud.Firestore.Timestamp>
    {
        public override Google.Cloud.Firestore.Timestamp Read(ref Utf8JsonReader reader, System.Type objectType, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected StartObject token for Timestamp");
            }

            long seconds = 0;
            long nanoseconds = 0;
            bool foundSeconds = false;
            bool foundNanoseconds = false;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected PropertyName token");
                }

                string propertyName = reader.GetString();
                reader.Read();

                if (propertyName == "seconds")
                {
                    seconds = reader.GetInt64();
                    foundSeconds = true;
                }
                else if (propertyName == "nanoseconds")
                {
                    nanoseconds = reader.GetInt64();
                    foundNanoseconds = true;
                }
            }

            if (!foundSeconds || !foundNanoseconds)
            {
                throw new JsonException("Missing seconds or nanoseconds in Timestamp");
            }


            // Convert seconds and nanoseconds to DateTime and then to Timestamp
            DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddSeconds(seconds)
                .AddTicks((long)(nanoseconds / 100)); // Convert nanoseconds to ticks (1 tick = 100 ns)
            return Google.Cloud.Firestore.Timestamp.FromDateTime(dateTime);
        }

        public override void Write(Utf8JsonWriter writer, Google.Cloud.Firestore.Timestamp value, JsonSerializerOptions options)
        {
            var protoTimestamp = value.ToProto(); // Convert to Protobuf Timestamp
            writer.WriteStartObject();
            writer.WriteNumber("seconds", protoTimestamp.Seconds);
            writer.WriteNumber("nanoseconds", protoTimestamp.Nanos);
            writer.WriteEndObject();
        }
    }
}