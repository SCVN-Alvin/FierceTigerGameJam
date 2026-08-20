using UnityEngine;

namespace GameJam.Data
{
    /// <summary>
    /// Where saved data physically goes. Behind an interface so tests, and any future move to a
    /// file or a server, do not have to touch anything that reads or writes user data.
    /// </summary>
    public interface ISaveStore
    {
        bool TryLoad(string key, out string json);

        void Save(string key, string json);

        void Delete(string key);

        /// <summary>Commits everything written so far. Cheap writes, expensive flush.</summary>
        void Flush();
    }

    /// <summary>
    /// PlayerPrefs, holding one JSON document per record. PlayerPrefs is the one storage that
    /// works unchanged on every target this game is likely to ship to, WebGL and playable ads
    /// included, where a file path does not exist.
    ///
    /// One key per record rather than a single blob: a record that fails to parse can be dropped
    /// and rebuilt on its own instead of taking the player's gold and map progress with it.
    /// </summary>
    public sealed class PlayerPrefsSaveStore : ISaveStore
    {
        private const string KeyPrefix = "gamejam.";

        public bool TryLoad(string key, out string json)
        {
            string storageKey = KeyPrefix + key;
            if (!PlayerPrefs.HasKey(storageKey))
            {
                json = null;
                return false;
            }

            json = PlayerPrefs.GetString(storageKey);
            return !string.IsNullOrEmpty(json);
        }

        public void Save(string key, string json)
        {
            PlayerPrefs.SetString(KeyPrefix + key, json);
        }

        public void Delete(string key)
        {
            PlayerPrefs.DeleteKey(KeyPrefix + key);
        }

        public void Flush()
        {
            PlayerPrefs.Save();
        }
    }
}
