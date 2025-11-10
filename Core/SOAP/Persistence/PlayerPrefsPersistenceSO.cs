using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using LegendaryTools.Persistence;
using UnityEngine;

namespace LegendaryTools.SOAP
{
    /// <summary>
    /// PlayerPrefs-backed persistence provider.
    /// - Supports primitives and common Unity structs (Vector2/3, Quaternion, Color).
    /// - Keys are human-readable and namespaced by Application.productName.
    /// Note: Unsupported types fall back to string WriteOnly (no recoverable read). See comments below.
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerPrefsPersistence", menuName = "Tools/SOAP/Persistence/PlayerPrefs Provider")]
    public class PlayerPrefsPersistenceSO : PersistenceProviderSO
    {
        private readonly List<Delegate> _listeners = new();
        private bool _busy;

        public override bool IsBusy => _busy;

        public override void Load()
        {
            // PlayerPrefs is available immediately; nothing to initialize.
        }

        public override void Save()
        {
            PlayerPrefs.Save();
        }

        public override Task LoadAsync()
        {
            Load();
            return Task.CompletedTask;
        }

        public override Task SaveAsync()
        {
            Save();
            return Task.CompletedTask;
        }

        public override string Set<T>(T dataToSave, object id = null, int version = 0, bool autoSave = false)
        {
            _busy = true;
            try
            {
                string key = BuildKey(id, typeof(T));
                bool existed = Has(key);

                WriteValue(key, dataToSave);

                Notify(existed ? PersistenceAction.Update : PersistenceAction.Add, key, dataToSave);
                if (autoSave) PlayerPrefs.Save();
                return key;
            }
            finally
            {
                _busy = false;
            }
        }

        public override T Get<T>(object id, T defaultValue = default)
        {
            string key = BuildKey(id, typeof(T));
            if (!Has(key)) return defaultValue;
            return ReadValue(key, defaultValue);
        }

        public override bool Delete<T>(object id, bool autoSave = false)
        {
            _busy = true;
            try
            {
                string key = BuildKey(id, typeof(T));
                if (!Has(key)) return false;
                PlayerPrefs.DeleteKey(key);
                Notify(PersistenceAction.Delete, key, default(T));
                if (autoSave) PlayerPrefs.Save();
                return true;
            }
            finally
            {
                _busy = false;
            }
        }

        public override bool Contains<T>(object id)
        {
            string key = BuildKey(id, typeof(T));
            return Has(key);
        }

        public override void AddListener<T>(Action<IPersistence, PersistenceAction, string, object> callback)
        {
            if (callback != null) _listeners.Add(callback);
        }

        public override void RemoveListener<T>(Action<IPersistence, PersistenceAction, string, object> callback)
        {
            if (callback == null) return;
            _listeners.Remove(callback);
        }

        // --------- Internal helpers ---------

        private static bool Has(string key)
        {
            return PlayerPrefs.HasKey(key);
        }

        private static void WriteValue<T>(string key, T value)
        {
            // Use direct PlayerPrefs types when possible.
            switch (value)
            {
                case int i:
                    PlayerPrefs.SetInt(key, i);
                    return;
                case float f:
                    PlayerPrefs.SetFloat(key, f);
                    return;
                case string s:
                    PlayerPrefs.SetString(key, s ?? string.Empty);
                    return;
                case bool b:
                    PlayerPrefs.SetInt(key, b ? 1 : 0);
                    return;
                case short sh:
                    PlayerPrefs.SetInt(key, sh);
                    return;
                case byte by:
                    PlayerPrefs.SetInt(key, by);
                    return;
                case long l:
                    PlayerPrefs.SetString(key, l.ToString(CultureInfo.InvariantCulture));
                    return;
                case double d:
                    PlayerPrefs.SetString(key, d.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case Vector2 v2:
                    PlayerPrefs.SetString(key,
                        $"{v2.x.ToString("R", CultureInfo.InvariantCulture)};{v2.y.ToString("R", CultureInfo.InvariantCulture)}");
                    return;
                case Vector3 v3:
                    PlayerPrefs.SetString(key,
                        $"{v3.x.ToString("R", CultureInfo.InvariantCulture)};{v3.y.ToString("R", CultureInfo.InvariantCulture)};{v3.z.ToString("R", CultureInfo.InvariantCulture)}");
                    return;
                case Quaternion q:
                    PlayerPrefs.SetString(key,
                        $"{q.x.ToString("R", CultureInfo.InvariantCulture)};{q.y.ToString("R", CultureInfo.InvariantCulture)};{q.z.ToString("R", CultureInfo.InvariantCulture)};{q.w.ToString("R", CultureInfo.InvariantCulture)}");
                    return;
                case Color c:
                    PlayerPrefs.SetString(key,
                        $"{c.r.ToString("R", CultureInfo.InvariantCulture)};{c.g.ToString("R", CultureInfo.InvariantCulture)};{c.b.ToString("R", CultureInfo.InvariantCulture)};{c.a.ToString("R", CultureInfo.InvariantCulture)}");
                    return;
            }

            // Fallback: write string representation (not JSON). WARNING: Not recoverable unless you add a custom reader.
            PlayerPrefs.SetString(key, value != null ? value.ToString() : string.Empty);
        }

        private static T ReadValue<T>(string key, T defaultValue)
        {
            Type t = typeof(T);

            // Strong mappings for common types.
            if (t == typeof(int))
                return (T)(object)PlayerPrefs.GetInt(key, Convert.ToInt32(defaultValue));

            if (t == typeof(float))
                return (T)(object)PlayerPrefs.GetFloat(key, Convert.ToSingle(defaultValue));

            if (t == typeof(string))
                return (T)(object)PlayerPrefs.GetString(key, defaultValue as string ?? string.Empty);

            if (t == typeof(bool))
                return (T)(object)(PlayerPrefs.GetInt(key, Convert.ToBoolean(defaultValue) ? 1 : 0) != 0);

            if (t == typeof(short))
                return (T)(object)(short)PlayerPrefs.GetInt(key, Convert.ToInt32(defaultValue));

            if (t == typeof(byte))
                return (T)(object)(byte)PlayerPrefs.GetInt(key, Convert.ToInt32(defaultValue));

            if (t == typeof(long))
            {
                string s = PlayerPrefs.GetString(key, Convert.ToString(defaultValue, CultureInfo.InvariantCulture));
                if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                    return (T)(object)l;
                return defaultValue;
            }

            if (t == typeof(double))
            {
                string s = PlayerPrefs.GetString(key, Convert.ToString(defaultValue, CultureInfo.InvariantCulture));
                if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture,
                        out double d))
                    return (T)(object)d;
                return defaultValue;
            }

            if (t == typeof(Vector2))
            {
                if (TryParseFloats(PlayerPrefs.GetString(key, ""), 2, out float[] arr))
                    return (T)(object)new Vector2(arr[0], arr[1]);
                return defaultValue;
            }

            if (t == typeof(Vector3))
            {
                if (TryParseFloats(PlayerPrefs.GetString(key, ""), 3, out float[] arr))
                    return (T)(object)new Vector3(arr[0], arr[1], arr[2]);
                return defaultValue;
            }

            if (t == typeof(Quaternion))
            {
                if (TryParseFloats(PlayerPrefs.GetString(key, ""), 4, out float[] arr))
                    return (T)(object)new Quaternion(arr[0], arr[1], arr[2], arr[3]);
                return defaultValue;
            }

            if (t == typeof(Color))
            {
                if (TryParseFloats(PlayerPrefs.GetString(key, ""), 4, out float[] arr))
                    return (T)(object)new Color(arr[0], arr[1], arr[2], arr[3]);
                return defaultValue;
            }

            // Fallback: no reader for custom types (write-only).
            return defaultValue;
        }

        private static bool TryParseFloats(string s, int expectedCount, out float[] values)
        {
            values = null;
            if (string.IsNullOrEmpty(s)) return false;
            string[] parts = s.Split(';');
            if (parts.Length != expectedCount) return false;

            values = new float[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture, out values[i]))
                    return false;
            }

            return true;
        }

        private void Notify(PersistenceAction action, string key, object payload)
        {
            if (_listeners.Count == 0) return;

            Delegate[] snapshot = _listeners.ToArray();
            foreach (Delegate del in snapshot)
            {
                try
                {
                    del.DynamicInvoke(this, action, key, payload);
                }
                catch
                {
                    /* Keep provider robust against listener errors. */
                }
            }
        }
    }
}