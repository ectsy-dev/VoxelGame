using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Windows;

public struct CharacterInput
{
    public Vector2 Move;
    public Quaternion Rotation;
    public bool Jump;
    public bool FlyToggle;
    public bool FlyDown;

}

public class PlayerController : MonoBehaviour
{

    [Header("Player Movement Variables")]
    [SerializeField] private float moveSpeed;
    [SerializeField] private float jumpForce;
    [SerializeField] private float fallMultiplier = 2.0f;
    [SerializeField] private float playerWidth;
    [SerializeField] private float playerHeight;
    private bool isGrounded;
    private bool isFlying;

    [Header("Player References")]
    [SerializeField] private Transform cameraTarget;
    [SerializeField] private World overWorld;

    private Vector3 moveDirection;
    private Vector3 velocity;
    private float gravity = 32f;

    public void Initialize()
    {
        
        cameraTarget = transform.Find("CameraTarget");
        if (cameraTarget == null)
            Debug.LogError("No Camera Target found on Player");

        overWorld = Object.FindFirstObjectByType<World>();
        if (overWorld == null)
            Debug.LogError("No World found in scene");

    }

    public void UpdateCharacter(CharacterInput input)
    {

        PlayerMovement(input);
        PlayerRotation(input);

    }

    private void PlayerMovement(CharacterInput input)
    {
        // Cap to 50ms so chunk-generation frame spikes don't cause tunneling
        float dt = Mathf.Min(Time.deltaTime, 0.05f);

        if (input.FlyToggle)
        {
            isFlying = !isFlying;
            velocity = Vector3.zero;
        }

        if (isFlying)
        {
            float flySpeed = moveSpeed * 4f;
            float flyVertical = 0f;
            if (input.Jump) flyVertical = flySpeed;
            if (input.FlyDown) flyVertical = -flySpeed;

            moveDirection = transform.forward * input.Move.y + transform.right * input.Move.x;
            moveDirection.Normalize();
            moveDirection = moveDirection * flySpeed * dt + Vector3.up * flyVertical * dt;
            transform.Translate(moveDirection, Space.World);
            return;
        }

        if (velocity.y < 0)
            velocity -= Vector3.up * gravity * fallMultiplier * dt;
        else
            velocity -= Vector3.up * gravity * dt;

        if (input.Jump && isGrounded)
        {

            velocity.y = jumpForce;
            isGrounded = false;

        }

        moveDirection = transform.forward * input.Move.y + transform.right * input.Move.x;
        moveDirection.Normalize();

        moveDirection *= moveSpeed * dt;
        moveDirection += velocity * dt;

        if((moveDirection.z > 0 && frontBlockCheck) || (moveDirection.z < 0 && backBlockCheck))
            moveDirection.z = 0;

        if((moveDirection.z > 0 && ((frontLeftBlockCheck && !leftBlockCheck) || (frontRightBlockCheck && !rightBlockCheck))) || (moveDirection.z < 0 && ((backLeftBlockCheck && !leftBlockCheck) || (backRightBlockCheck && !rightBlockCheck))))
            moveDirection.z = 0;

        if ((moveDirection.x > 0 && rightBlockCheck) || (moveDirection.x < 0 && leftBlockCheck))
            moveDirection.x = 0;

        if ((moveDirection.x > 0 && ((frontRightBlockCheck && !frontBlockCheck) || (backRightBlockCheck && !backBlockCheck))) || (moveDirection.x < 0 && ((frontLeftBlockCheck && !frontBlockCheck) || (backLeftBlockCheck && !backBlockCheck))))
            moveDirection.x = 0;

        if (moveDirection.y < 0)
            moveDirection.y = checkDownSpeed(moveDirection.y);
        else if(moveDirection.y > 0)
            moveDirection.y = checkUpSpeed(moveDirection.y);

        transform.Translate(moveDirection, Space.World);

    }

    private void PlayerRotation(CharacterInput input)
    {

        transform.rotation = Quaternion.Euler(0, input.Rotation.eulerAngles.y, 0);

    }

    private float checkDownSpeed(float downSpeed)
    {

        if(overWorld.CheckForVoxel(transform.position.x - playerWidth, transform.position.y - playerHeight + downSpeed, transform.position.z - playerWidth) && (!leftBlockCheck && !backBlockCheck && !backLeftBlockCheck) ||
           overWorld.CheckForVoxel(transform.position.x + playerWidth, transform.position.y - playerHeight + downSpeed, transform.position.z - playerWidth) && (!rightBlockCheck && !backBlockCheck && !backRightBlockCheck) ||
           overWorld.CheckForVoxel(transform.position.x + playerWidth, transform.position.y - playerHeight + downSpeed, transform.position.z + playerWidth) && (!rightBlockCheck && !frontBlockCheck && !frontRightBlockCheck) ||
           overWorld.CheckForVoxel(transform.position.x - playerWidth, transform.position.y - playerHeight + downSpeed, transform.position.z + playerWidth) && (!leftBlockCheck && !frontBlockCheck && !frontLeftBlockCheck))
        {
            isGrounded = true;
            velocity.y = 0;
            return 0;
        }
        else
        {
            isGrounded = false;
            return downSpeed;
        }

    }

    private float checkUpSpeed(float upSpeed)
    {

        if(overWorld.CheckForVoxel(transform.position.x - playerWidth, transform.position.y + playerHeight - 0.1f + upSpeed, transform.position.z - playerWidth) && (!leftBlockCheck && !backBlockCheck && !backLeftBlockCheck) ||
            overWorld.CheckForVoxel(transform.position.x + playerWidth, transform.position.y + playerHeight - 0.1f + upSpeed, transform.position.z - playerWidth) && (!rightBlockCheck && !backBlockCheck && !backRightBlockCheck) ||
            overWorld.CheckForVoxel(transform.position.x + playerWidth, transform.position.y + playerHeight - 0.1f + upSpeed, transform.position.z + playerWidth) && (!rightBlockCheck && !frontBlockCheck && !frontRightBlockCheck) ||
            overWorld.CheckForVoxel(transform.position.x - playerWidth, transform.position.y + playerHeight - 0.1f + upSpeed, transform.position.z + playerWidth) && (!leftBlockCheck && !frontBlockCheck && !frontLeftBlockCheck))
        {

            velocity.y = 0;
            return 0;

        }
        else
        {

            return upSpeed;

        }

    }

    public bool frontBlockCheck
    {

        get
        {

            if (overWorld.CheckForVoxel(transform.position.x, transform.position.y - playerHeight, transform.position.z + playerWidth + 0.2f) ||
                overWorld.CheckForVoxel(transform.position.x, transform.position.y + playerHeight - 0.1f, transform.position.z + playerWidth + 0.2f))
                return true;
            else
                return false;

        }

    }

    public bool backBlockCheck
    {

        get
        {

            if (overWorld.CheckForVoxel(transform.position.x, transform.position.y - playerHeight, transform.position.z - playerWidth - 0.2f) ||
                overWorld.CheckForVoxel(transform.position.x, transform.position.y + playerHeight - 0.1f, transform.position.z - playerWidth - 0.2f))
                return true;
            else
                return false;

        }

    }

    public bool leftBlockCheck
    {

        get
        {

            if (overWorld.CheckForVoxel(transform.position.x - playerWidth - 0.2f, transform.position.y - playerHeight, transform.position.z) ||
                overWorld.CheckForVoxel(transform.position.x - playerWidth - 0.2f, transform.position.y + playerHeight - 0.1f, transform.position.z))
                return true;
            else
                return false;

        }

    }

    public bool rightBlockCheck
    {

        get
        {

            if (overWorld.CheckForVoxel(transform.position.x + playerWidth + 0.2f, transform.position.y - playerHeight, transform.position.z) ||
                overWorld.CheckForVoxel(transform.position.x + playerWidth + 0.2f, transform.position.y + playerHeight - 0.1f, transform.position.z))
                return true;
            else
                return false;

        }

    }

    public bool frontLeftBlockCheck
    {
        get
        {
            if (overWorld.CheckForVoxel(transform.position.x - playerWidth - 0.2f, transform.position.y - playerHeight, transform.position.z + playerWidth + 0.2f) ||
                overWorld.CheckForVoxel(transform.position.x - playerWidth - 0.2f, transform.position.y + playerHeight - 0.1f, transform.position.z + playerWidth + 0.2f))
                return true;
            else
                return false;
        }
    }

    public bool frontRightBlockCheck
    {
        get
        {
            if (overWorld.CheckForVoxel(transform.position.x + playerWidth + 0.2f, transform.position.y - playerHeight, transform.position.z + playerWidth + 0.2f) ||
                overWorld.CheckForVoxel(transform.position.x + playerWidth + 0.2f, transform.position.y + playerHeight - 0.1f, transform.position.z + playerWidth + 0.2f))
                return true;
            else
                return false;
        }
    }

    public bool backLeftBlockCheck
    {
        get
        {
            if (overWorld.CheckForVoxel(transform.position.x - playerWidth - 0.2f, transform.position.y - playerHeight, transform.position.z - playerWidth - 0.2f) ||
                overWorld.CheckForVoxel(transform.position.x - playerWidth - 0.2f, transform.position.y + playerHeight - 0.1f, transform.position.z - playerWidth - 0.2f))
                return true;
            else
                return false;
        }
    }

    public bool backRightBlockCheck
    {
        get
        {
            if (overWorld.CheckForVoxel(transform.position.x + playerWidth + 0.2f, transform.position.y - playerHeight, transform.position.z - playerWidth - 0.2f) ||
                overWorld.CheckForVoxel(transform.position.x + playerWidth + 0.2f, transform.position.y + playerHeight - 0.1f, transform.position.z - playerWidth - 0.2f))
                return true;
            else
                return false;
        }
    }

    public float PlayerHeight => playerHeight;

    public Transform GetCameraTarget() => cameraTarget;

}
