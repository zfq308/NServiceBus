namespace NServiceBus
{
    using System;
    using System.Collections.Generic;
    using Serialization;
    using Settings;

    static class SerializationSettingsExtensions
    {
        const string AdditionalSerializersSettingsKey = "AdditionalDeserializers";

        public static void ApplySerializationDefaults(this SettingsHolder settings)
        {
            settings.SetDefault(AdditionalSerializersSettingsKey, new List<Tuple<SerializationDefinition, SettingsHolder>>());
        }

        public static List<Tuple<SerializationDefinition, SettingsHolder>> GetAdditionalSerializers(this SettingsHolder settings)
        {
            List<Tuple<SerializationDefinition, SettingsHolder>> deserializers;
            if (!settings.TryGet(AdditionalSerializersSettingsKey, out deserializers))
            {
                deserializers = new List<Tuple<SerializationDefinition, SettingsHolder>>();
                settings.Set(AdditionalSerializersSettingsKey, deserializers);
            }
            return deserializers;
        }

        public static List<Tuple<SerializationDefinition, SettingsHolder>> GetAdditionalSerializers(this ReadOnlySettings settings)
        {
            return settings.Get<List<Tuple<SerializationDefinition, SettingsHolder>>>(AdditionalSerializersSettingsKey);
        }

        public static void SetMainSerializer(this SettingsHolder settings, SerializationDefinition definition, SettingsHolder serializerSpecificSettings)
        {
            settings.Set<Tuple<SerializationDefinition, SettingsHolder>>(Tuple.Create(definition, serializerSpecificSettings));
        }

        public static Tuple<SerializationDefinition, SettingsHolder> GetMainSerializer(this ReadOnlySettings settings)
        {
            Tuple<SerializationDefinition, SettingsHolder> defaultSerializerAndSettings;
            if (!settings.TryGet(out defaultSerializerAndSettings))
            {
                defaultSerializerAndSettings = Tuple.Create<SerializationDefinition, SettingsHolder>(new XmlSerializer(), new SettingsHolder());
            }
            return defaultSerializerAndSettings;
        }
    }
}