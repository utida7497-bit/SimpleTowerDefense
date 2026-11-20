using System.Collections.Generic;
using UnityEngine;

public class TowerRange : MonoBehaviour
{
    [SerializeField] private Tower data;
    public List<GameObject> enemyList = new();

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Enemy"))
        {
            Debug.Log(other.gameObject + "が当たった！");
            if (!enemyList.Contains(other.gameObject)) 
            {
                enemyList.Add(other.gameObject);
            }
        }
    }
    private void OnTriggerExit(Collider other)
    {
        enemyList.Remove(other.gameObject);
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
