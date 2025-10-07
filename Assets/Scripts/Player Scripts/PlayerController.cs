using UnityEngine;

public struct CharacterInput
{
    public Vector2 Move;
    public Quaternion Rotation;

}

public class PlayerController : MonoBehaviour
{

    [SerializeField] private Transform cameraTarget;

    public void Initialize()
    {
        


    }

    public void UpdateInput(CharacterInput input)
    {

        transform.rotation = Quaternion.Euler(0, input.Rotation.eulerAngles.y, 0);

    }

    public Transform GetCameraTarget() => cameraTarget;

}
