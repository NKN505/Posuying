using UnityEngine;

public class HealPickup : MonoBehaviour
{
    public float healAmount = 250f;

    void OnTriggerEnter(Collider other)
    {
        Character character = other.GetComponent<Character>();
        if (character != null && character.GetIsPlayer())
        {
            character.Heal(healAmount);
            Destroy(gameObject);
        }
    }
}
