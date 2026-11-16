using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SavableObservable.Tests.Contracts {
    public enum PrefKeyType {
        Int,
        Float,
        String
    }

    /// <summary>
    /// Reusable sandbox utility for isolating PlayerPrefs in unit and integration tests.
    /// Captures explicitly typed tracked keys before a test, allows testing against defaults or custom values,
    /// and restores original player preferences and filesystem state upon disposal or End().
    /// </summary>
    public sealed class PrefsSandboxUtility : IDisposable {
        private readonly Dictionary<string, PrefKeyType> _trackedKeys = new Dictionary<string, PrefKeyType>();
        private readonly Dictionary<string, (int intVal, float floatVal, string strVal, int valType)> _backup
            = new Dictionary<string, (int, float, string, int)>();
        private readonly string _suppliedTempDirectory;
        private readonly string _ownedScratchDirectory;
        private bool _isActive;
        private bool _disposed;
        private bool _createdScratchDirectory;

        // valType: 1 = int, 2 = float, 3 = string, 0 = absent

        public PrefsSandboxUtility(
            IEnumerable<KeyValuePair<string, Type>> trackedKeys,
            string tempDirectory = null)
        {
            if (trackedKeys != null) {
                foreach (var kvp in trackedKeys) {
                    if (kvp.Key == null) continue;
                    _trackedKeys[kvp.Key] = ConvertTypeToPrefKeyType(kvp.Value, kvp.Key);
                }
            }

            _suppliedTempDirectory = tempDirectory;
            if (!string.IsNullOrEmpty(tempDirectory)) {
                _ownedScratchDirectory = Path.Combine(tempDirectory, "PrefsSandboxScratch_" + Guid.NewGuid().ToString("N"));
            }
        }

        public PrefsSandboxUtility(
            IEnumerable<KeyValuePair<string, PrefKeyType>> trackedKeys,
            string tempDirectory = null)
        {
            if (trackedKeys != null) {
                foreach (var kvp in trackedKeys) {
                    if (kvp.Key == null) continue;
                    _trackedKeys[kvp.Key] = kvp.Value;
                }
            }

            _suppliedTempDirectory = tempDirectory;
            if (!string.IsNullOrEmpty(tempDirectory)) {
                _ownedScratchDirectory = Path.Combine(tempDirectory, "PrefsSandboxScratch_" + Guid.NewGuid().ToString("N"));
            }
        }

        public PrefsSandboxUtility(
            IEnumerable<string> intKeys = null,
            IEnumerable<string> floatKeys = null,
            IEnumerable<string> stringKeys = null,
            string tempDirectory = null)
        {
            if (intKeys != null) {
                foreach (var key in intKeys) {
                    if (key != null) _trackedKeys[key] = PrefKeyType.Int;
                }
            }
            if (floatKeys != null) {
                foreach (var key in floatKeys) {
                    if (key != null) _trackedKeys[key] = PrefKeyType.Float;
                }
            }
            if (stringKeys != null) {
                foreach (var key in stringKeys) {
                    if (key != null) _trackedKeys[key] = PrefKeyType.String;
                }
            }

            _suppliedTempDirectory = tempDirectory;
            if (!string.IsNullOrEmpty(tempDirectory)) {
                _ownedScratchDirectory = Path.Combine(tempDirectory, "PrefsSandboxScratch_" + Guid.NewGuid().ToString("N"));
            }
        }

        public bool IsActive => _isActive;
        public string TempDirectory => _suppliedTempDirectory;
        public string ScratchDirectory => _ownedScratchDirectory;
        public IReadOnlyDictionary<string, PrefKeyType> TrackedKeys => _trackedKeys;

        public void Begin() {
            if (_disposed) throw new ObjectDisposedException(nameof(PrefsSandboxUtility));
            if (_isActive) return;
            _backup.Clear();

            foreach (var kvp in _trackedKeys) {
                string key = kvp.Key;
                PrefKeyType keyType = kvp.Value;

                if (PlayerPrefs.HasKey(key)) {
                    switch (keyType) {
                        case PrefKeyType.Int:
                            _backup[key] = (PlayerPrefs.GetInt(key), 0f, null, 1);
                            break;
                        case PrefKeyType.Float:
                            _backup[key] = (0, PlayerPrefs.GetFloat(key), null, 2);
                            break;
                        case PrefKeyType.String:
                            _backup[key] = (0, 0f, PlayerPrefs.GetString(key), 3);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(keyType), $"Unsupported key type: {keyType}");
                    }
                } else {
                    _backup[key] = (0, 0f, null, 0);
                }
            }

            if (!string.IsNullOrEmpty(_ownedScratchDirectory) && !Directory.Exists(_ownedScratchDirectory)) {
                Directory.CreateDirectory(_ownedScratchDirectory);
                _createdScratchDirectory = true;
            }

            _isActive = true;
        }

        public void ClearTrackedKeys() {
            if (!_isActive) {
                throw new InvalidOperationException("PrefsSandboxUtility must be started via Begin() before clearing.");
            }
            foreach (var key in _trackedKeys.Keys) {
                PlayerPrefs.DeleteKey(key);
            }
            PlayerPrefs.Save();
        }

        public void End() {
            if (!_isActive) return;

            foreach (var kvp in _backup) {
                string key = kvp.Key;
                var data = kvp.Value;
                switch (data.valType) {
                    case 1:
                        PlayerPrefs.SetInt(key, data.intVal);
                        break;
                    case 2:
                        PlayerPrefs.SetFloat(key, data.floatVal);
                        break;
                    case 3:
                        PlayerPrefs.SetString(key, data.strVal);
                        break;
                    default:
                        PlayerPrefs.DeleteKey(key);
                        break;
                }
            }
            PlayerPrefs.Save();

            // Strictly preserve pre-existing temp directory: only delete our unique owned scratch directory if created
            if (_createdScratchDirectory && !string.IsNullOrEmpty(_ownedScratchDirectory) && Directory.Exists(_ownedScratchDirectory)) {
                try {
                    Directory.Delete(_ownedScratchDirectory, true);
                } catch {
                    // Best-effort cleanup of owned scratch dir
                }
            }

            _isActive = false;
            _createdScratchDirectory = false;
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            End();
        }

        private static PrefKeyType ConvertTypeToPrefKeyType(Type type, string keyName) {
            if (type == null) {
                throw new ArgumentNullException(nameof(type), $"Tracked key '{keyName}' must have a non-null Type.");
            }
            if (type == typeof(int) || type == typeof(short) || type == typeof(byte) || type == typeof(sbyte) || type == typeof(bool)) {
                return PrefKeyType.Int;
            }
            if (type == typeof(float) || type == typeof(double)) {
                return PrefKeyType.Float;
            }
            if (type == typeof(string)) {
                return PrefKeyType.String;
            }

            throw new NotSupportedException($"Type '{type.FullName}' for key '{keyName}' is not supported by PlayerPrefs. Use int, float, or string.");
        }
    }
}
