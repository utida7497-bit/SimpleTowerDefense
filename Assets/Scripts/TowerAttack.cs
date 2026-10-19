using UnityEngine;

public class TowerAttack : MonoBehaviour
{
    [SerializeField] private Tower data;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Enemy"))
        {

        }
    }
    private void OnDrawGizmos()
    {
        Vector3 gizmoPosition = transform.position;
        gizmoPosition.y = 1f;
        UnityEditor.Handles.color = Color.red;
        UnityEditor.Handles.DrawWireDisc(gizmoPosition,Vector3.up,data.attackRadius);
    }
}
