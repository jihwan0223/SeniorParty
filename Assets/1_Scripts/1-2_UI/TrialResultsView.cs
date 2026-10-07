using System.Collections.Generic;
using UnityEngine.UIElements;

namespace SeniorParty
{
    // 결과창의 "N회차" + O/X(중간에 나가서 모르면 ?) 블록을 그린다. 여러 게임이 같이 쓴다.
    // 회차가 적을수록 칸을 크게 그려서(trial-size-lg/md), 2~3회 짜리 게임에서 너무 작아 보이지 않게 한다.
    public static class TrialResultsView
    {
        // labels: 칸 위에 보여줄 글자를 직접 정하고 싶을 때(예: "레벨 3 · 1회차"). 생략하면 "N회차".
        public static void Build(VisualElement container, IReadOnlyList<bool?> results, IReadOnlyList<string> labels = null)
        {
            container.Clear();
            string sizeClass = results.Count <= 3 ? "trial-size-lg" : results.Count <= 8 ? "trial-size-md" : null;

            for (int i = 0; i < results.Count; i++)
            {
                bool? result = results[i];
                var col = new VisualElement();
                col.AddToClassList("trial-col");

                var roundLabel = new Label(labels != null && i < labels.Count ? labels[i] : $"{i + 1}회차");
                roundLabel.AddToClassList("trial-round-label");
                if (sizeClass != null)
                {
                    roundLabel.AddToClassList(sizeClass);
                }
                col.Add(roundLabel);

                var mark = new Label(result == null ? "?" : result.Value ? "O" : "X");
                mark.AddToClassList("trial-mark");
                mark.AddToClassList(result == null ? "unknown" : result.Value ? "correct" : "wrong");
                if (sizeClass != null)
                {
                    mark.AddToClassList(sizeClass);
                }
                col.Add(mark);

                container.Add(col);
            }
        }
    }
}
