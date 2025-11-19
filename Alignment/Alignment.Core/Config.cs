using System;
using System.IO;
using Newtonsoft.Json;

namespace Alignment.Core
{
    public interface IAlignmentConfig
    {
        void SaveParams(AlignmentParams p);
        AlignmentParams LoadParams();
        void SaveConstants(AlignmentConstants c);
        AlignmentConstants LoadConstants();
    }

    public sealed class JsonAlignmentConfig : IAlignmentConfig
    {
        readonly string _dir;
        public JsonAlignmentConfig(string baseDir = null)
        {
            _dir = baseDir ?? Path.Combine(@"C:\Users\Public\Documents\wisetech", "AlignmentData");
            Directory.CreateDirectory(_dir);
        }

        public void SaveParams(AlignmentParams p) => Write(Path.Combine(_dir, "Params.json"), p);
        public AlignmentParams LoadParams() => Read<AlignmentParams>(Path.Combine(_dir, "Params.json")) ?? new AlignmentParams();

        public void SaveConstants(AlignmentConstants c) => Write(Path.Combine(_dir, "Constants.json"), c);
        public AlignmentConstants LoadConstants() => Read<AlignmentConstants>(Path.Combine(_dir, "Constants.json")) ?? new AlignmentConstants();

        static T Read<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<T>(json);
        }
        static void Write<T>(string path, T obj)
        {
            var json = JsonConvert.SerializeObject(obj, Formatting.Indented);
            File.WriteAllText(path, json);
        }
    }
}
