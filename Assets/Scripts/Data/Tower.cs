using UnityEngine;

[CreateAssetMenu(fileName = "Tower", menuName = "Data/Tower")]
public class Tower : ScriptableObject
{
    public Tower.TowerType towerType;
    public enum TowerType
    {
        normal = 0,
        fire = 1,
        freeze = 2,
        laser = 3,
    }

    public float attackRadius = 10f;
    public float attackCooldown = 3f;

}
