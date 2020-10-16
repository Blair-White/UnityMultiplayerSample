using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class PlayerController : MonoBehaviour
{
    [SerializeField]
    float moveSpeed = 0f;

    public bool IsClient; // the one already in the scene when we start
    public bool isDropped;


    // Use this for initialization
    void Start()
    {

    }
    void Update()
    {
        if (Input.GetKey(KeyCode.UpArrow))
        {
            transform.Translate(Vector3.forward / 36);
        }

        if (Input.GetKey(KeyCode.DownArrow))
        {
            transform.Translate(-Vector3.forward / 36);
        }

        if (Input.GetKey(KeyCode.LeftArrow))
        {
            transform.Translate(Vector3.left / 36);
        }

        if (Input.GetKey(KeyCode.RightArrow))
        {
            transform.Translate(-Vector3.left / 36);
        }
    }


}