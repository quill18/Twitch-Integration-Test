using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FallingUsername : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        velocity = new Vector2(0, -500);
    }

    private Vector2 velocity;
    private float lifespan = 5f;

    // Update is called once per frame
    void Update()
    {
        lifespan -= Time.deltaTime;
        if (lifespan < 0)
        {
            Destroy(gameObject);
            return;
        }

        transform.Translate(velocity * Time.deltaTime);
    }
}
