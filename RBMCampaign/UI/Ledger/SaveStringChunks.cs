using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace RBMCampaign
{
    // The save system writes every entry's length as a 16-bit number (ContainerSaveData.WriteElementEntry),
    // so a string over 32,767 bytes corrupts the save. Long values are cut into pieces under
    // "key", "key\u00011", "key\u00012"... for the save only, and joined again on load.
    internal static class SaveStringChunks
    {
        // UTF-16 chars; a char is at most 3 UTF-8 bytes, so a piece stays under 30,000 bytes.
        internal const int MaxChars = 10000;
        private const char Marker = '\u0001';

        internal static void Sync(IDataStore dataStore, string key, ref Dictionary<string, string> dict)
        {
            Dictionary<string, string> stored = dataStore.IsSaving && dict != null ? Split(dict) : dict;
            dataStore.SyncData(key, ref stored);
            if (dataStore.IsLoading)
            {
                dict = stored != null ? Join(stored) : null;
            }
        }

        // The same dictionary when every value fits; otherwise a copy with the long values cut up.
        internal static Dictionary<string, string> Split(Dictionary<string, string> dict)
        {
            bool tooLong = false;
            foreach (string value in dict.Values)
            {
                if (value != null && value.Length > MaxChars)
                {
                    tooLong = true;
                    break;
                }
            }
            if (!tooLong)
            {
                return dict;
            }

            var result = new Dictionary<string, string>(dict.Count + 8);
            foreach (KeyValuePair<string, string> kv in dict)
            {
                string value = kv.Value;
                if (value == null || value.Length <= MaxChars)
                {
                    result[kv.Key] = value;
                    continue;
                }
                int piece = 0;
                for (int start = 0; start < value.Length; piece++)
                {
                    int length = System.Math.Min(MaxChars, value.Length - start);
                    // Never cut a surrogate pair in half: UTF-8 cannot encode a lone half.
                    if (start + length < value.Length && char.IsHighSurrogate(value[start + length - 1]))
                    {
                        length--;
                    }
                    result[piece == 0 ? kv.Key : kv.Key + Marker + piece] = value.Substring(start, length);
                    start += length;
                }
            }
            return result;
        }

        // The same dictionary when nothing was cut up; otherwise a copy with the pieces joined.
        internal static Dictionary<string, string> Join(Dictionary<string, string> dict)
        {
            bool pieces = false;
            foreach (string key in dict.Keys)
            {
                if (key.IndexOf(Marker) >= 0)
                {
                    pieces = true;
                    break;
                }
            }
            if (!pieces)
            {
                return dict;
            }

            var result = new Dictionary<string, string>(dict.Count);
            foreach (KeyValuePair<string, string> kv in dict)
            {
                if (kv.Key.IndexOf(Marker) >= 0)
                {
                    continue;
                }
                string value = kv.Value;
                string next;
                for (int piece = 1; dict.TryGetValue(kv.Key + Marker + piece, out next); piece++)
                {
                    value += next;
                }
                result[kv.Key] = value;
            }
            return result;
        }
    }
}
