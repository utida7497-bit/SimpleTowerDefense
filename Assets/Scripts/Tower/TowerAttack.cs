using UnityEngine;
using Cysharp.Threading.Tasks;

public class TowerAttack : MonoBehaviour
{
    TowerRange towerRange;
    private void Awake()
    {
        towerRange = GetComponentInChildren<TowerRange>();        
    }

    private void Start()
    {
        Attack().Forget();
    }

    private async UniTask Attack()
    {
        while (true) {
            await UniTask.WaitUntil(() => towerRange.enemyList.Count > 0);
            if(towerRange.enemyList.Count <= 0)continue;
            Debug.Log(towerRange.enemyList[0]+"を攻撃！");
            await UniTask.Delay(3000);
        }
    }
}
