namespace Framework.Data.Entity
{
    /// <summary>
    /// Implemented by each game's project-specific persistent save model.
    /// </summary>
    public interface ISaveData
    {
        int SchemaVersion { get; }
        void Validate();
    }
}
