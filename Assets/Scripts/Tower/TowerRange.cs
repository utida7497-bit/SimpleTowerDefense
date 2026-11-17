using UnityEngine;
using VContainer;

public class TowerRange : MonoBehaviour
{
    [SerializeField] private Tower data;

    [Inject] private TowerAttack towerAttack;
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Enemy"))
        {
            Debug.Log("敵が当たった！");
        }
    }
    /// <summary> 範囲円のデバッグ用 </summary>
    private void OnDrawGizmos()
    {
        Vector3 gizmoPosition = transform.position;
        gizmoPosition.y = 1f;
        UnityEditor.Handles.color = Color.red;
        UnityEditor.Handles.DrawWireDisc(gizmoPosition, Vector3.up, data.attackRadius);
    }
}
