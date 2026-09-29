using System.Collections.Generic;
using Framework.Data.Entity;

namespace Framework.Data
{
    /// <summary>Game-specific mapping between a save record and Firestore fields.</summary>
    public interface IFirestoreSaveCodec
    {
        Dictionary<string, object> Encode(SaveRecord record);
        SaveRecord Decode(IDictionary<string, object> fields);
    }
}
