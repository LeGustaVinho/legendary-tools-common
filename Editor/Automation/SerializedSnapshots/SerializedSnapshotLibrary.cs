using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    [FilePath("UserSettings/LegendarySerializedSnapshots.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class SerializedSnapshotLibrary : ScriptableSingleton<SerializedSnapshotLibrary>,
        ISerializationCallbackReceiver
    {
        [SerializeField] private List<SerializedSnapshotRecord> _snapshots = new();

        public IReadOnlyList<SerializedSnapshotRecord> Snapshots => _snapshots;

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            RemoveInvalidEntries();
        }

        public SerializedSnapshotRecord Get(string snapshotId)
        {
            if (string.IsNullOrWhiteSpace(snapshotId))
                return null;

            return _snapshots.FirstOrDefault(snapshot => snapshot != null && snapshot.Id == snapshotId);
        }

        public SerializedSnapshotRecord Add(SerializedSnapshotRecord snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            snapshot.Id ??= Guid.NewGuid().ToString("N");
            _snapshots.Insert(0, snapshot);
            Save(true);
            return snapshot;
        }

        public bool Delete(string snapshotId)
        {
            SerializedSnapshotRecord snapshot = Get(snapshotId);
            if (snapshot == null)
                return false;

            _snapshots.Remove(snapshot);
            Save(true);
            return true;
        }

        public bool Rename(string snapshotId, string newName)
        {
            SerializedSnapshotRecord snapshot = Get(snapshotId);
            if (snapshot == null)
                return false;

            snapshot.Name = string.IsNullOrWhiteSpace(newName) ? snapshot.Name : newName.Trim();
            Save(true);
            return true;
        }

        public SerializedSnapshotRecord Duplicate(string snapshotId)
        {
            SerializedSnapshotRecord source = Get(snapshotId);
            if (source == null)
                return null;

            SerializedSnapshotRecord clone = JsonUtility.FromJson<SerializedSnapshotRecord>(
                JsonUtility.ToJson(source));

            clone.Id = Guid.NewGuid().ToString("N");
            clone.Name = $"{source.Name} Copy";
            clone.CapturedAtUtcTicks = DateTime.UtcNow.Ticks;

            _snapshots.Insert(0, clone);
            Save(true);
            return clone;
        }

        public void Clear()
        {
            _snapshots.Clear();
            Save(true);
        }

        private void RemoveInvalidEntries()
        {
            _snapshots ??= new List<SerializedSnapshotRecord>();
            _snapshots.RemoveAll(snapshot => snapshot == null || string.IsNullOrWhiteSpace(snapshot.Id));
        }
    }
}
