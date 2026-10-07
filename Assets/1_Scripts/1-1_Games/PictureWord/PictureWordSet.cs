using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeniorParty
{
    // 그림-단어 짝짓기 게임의 문항 목록. 그림(picture)은 비워둬도 게임은 돌아간다(그림 자리에 안내 문구만 나옴).
    [CreateAssetMenu(menuName = "SeniorParty/Picture Word Set", fileName = "PictureWordSet")]
    public class PictureWordSet : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public Sprite picture;
            public string word;
            // 무관한 오답은 다른 카테고리 단어에서, 유사 오답은 similarWords와 같은 카테고리 단어에서 뽑는다.
            public string category;
            // 기획서 3.3 표의 단어 빈도 구간 = 이 단어가 나오는 난이도(1 고빈도 ~ 10 최저빈도).
            [Range(1, 10)] public int level = 1;
            // 그림만 보고 헷갈릴 만한 단어(모양·쓰임이 비슷한 것).
            public string[] similarWords;
        }

        public List<Entry> entries = new();
    }
}
