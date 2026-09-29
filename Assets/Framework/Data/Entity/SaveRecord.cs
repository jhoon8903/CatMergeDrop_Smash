namespace Framework.Data.Entity
{
    /// <summary>
    /// Provider-independent serialized save envelope.
    /// </summary>
    public sealed class SaveRecord
    {
        public SaveRecord(int schemaVersion, long savedAtUnixMilliseconds, string payload)
        {
            SchemaVersion = schemaVersion;
            SavedAtUnixMilliseconds = savedAtUnixMilliseconds;
            Payload = payload;
        }

        public int SchemaVersion { get; }
        public long SavedAtUnixMilliseconds { get; }
        public string Payload { get; }
    }
}
