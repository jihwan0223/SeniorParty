using System.IO;
using UnityEngine;

namespace SeniorParty
{
    // SaveData를 파일(JSON)로 저장/불러온다. 임시 파일 -> 원자적 교체 + .bak 백업으로 저장 도중
    // 앱이 꺼져도(모바일에서 흔함) 파일이 깨지지 않게 한다.
    public static class SaveManager
    {
        private static readonly string SavePath = Path.Combine(Application.persistentDataPath, "save.json");
        private static readonly string BackupPath = Path.Combine(Application.persistentDataPath, "save.json.bak");
        private static readonly string TempPath = Path.Combine(Application.persistentDataPath, "save.json.tmp");

        // save.json을 읽고, 깨져 있으면 .bak에서 복구한다. 둘 다 없으면 기본값을 반환한다.
        public static SaveData Load()
        {
            if (TryLoadFrom(SavePath, out var data))
            {
                return data;
            }

            Debug.LogWarning("save.json을 읽지 못해 백업(.bak)에서 복구를 시도합니다.");
            if (TryLoadFrom(BackupPath, out data))
            {
                // 백업이 정상이었다면 메인 파일도 이 내용으로 바로 복구해둔다.
                Save(data);
                return data;
            }

            Debug.LogWarning("백업도 없거나 손상되어 기본값으로 시작합니다.");
            return new SaveData();
        }

        // 임시 파일에 먼저 쓴 뒤 원자적으로 교체한다(기존 파일은 .bak으로 밀려남).
        public static void Save(SaveData data)
        {
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(TempPath, json);

            if (File.Exists(SavePath))
            {
                File.Replace(TempPath, SavePath, BackupPath);
            }
            else
            {
                // 첫 저장이라 교체할 기존 파일이 없다.
                File.Move(TempPath, SavePath);
            }
        }

        // 지정한 경로에서 SaveData를 읽어본다. 실패하면 false.
        private static bool TryLoadFrom(string path, out SaveData data)
        {
            data = null;
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);
                data = JsonUtility.FromJson<SaveData>(json);
                return data != null;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"{path} 파싱 실패: {e.Message}");
                return false;
            }
        }
    }
}
