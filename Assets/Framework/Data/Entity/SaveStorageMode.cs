namespace Framework.Data.Entity
{
    /// <summary>
    /// Selected once by the app composition root;
    /// save models do not depend on this choice.
    /// </summary>
    public enum SaveStorageMode : byte
    {
        Local = 0,
        Network = 1
    }
}
