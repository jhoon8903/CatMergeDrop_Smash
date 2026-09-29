using System;
using UnityEngine;

namespace Framework.Data.Entity
{
    /// <summary>Each game supplies its save type, stable key, and starting state.</summary>
    public abstract class SaveDataDefinition : ScriptableObject
    {
        public abstract string SaveKey { get; }
        public abstract Type DataType { get; }
        public abstract ISaveData CreateDefault();
    }
}
