using System;
using UnityEngine;

public struct CameraInput
{

    public Vector2 Look;

}

public class PlayerCamera : MonoBehaviour
{

    [SerializeField] private float sensitivity = 0.1f;

    private Vector3 eulerAngles;

    internal void Initialize(Transform target)
    {

        transform.position = target.position;
        transform.eulerAngles = eulerAngles = target.eulerAngles;

    }

    public void UpdateRotation(CameraInput input)
    {

        eulerAngles += new Vector3(-input.Look.y, input.Look.x) * sensitivity;

        eulerAngles.x = Mathf.Clamp(eulerAngles.x, -90f, 90f);

        transform.eulerAngles = eulerAngles;

    }

    public void UpdatePosition(Transform target)
    {

        transform.position = target.position;

    }

}
