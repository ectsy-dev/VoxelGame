using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerManager : MonoBehaviour
{

    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerCamera playerCamera;

    private PlayerInputActions inputActions;
    private World world;

    private void Start()
    {

        Cursor.lockState = CursorLockMode.Locked;

        inputActions = new PlayerInputActions();
        inputActions.Enable();

        world = Object.FindFirstObjectByType<World>();

        playerController.Initialize();

        playerCamera.Initialize(playerController.GetCameraTarget());

    }

    private void OnDestroy()
    {

        inputActions.Dispose();

    }

    private void FixedUpdate()
    {



    }

    private void Update()
    {

        var input = inputActions.Gameplay;

        var cameraInput = new CameraInput
        {

            Look = input.Look.ReadValue<Vector2>()

        };

        playerCamera.UpdateRotation(cameraInput);

        var characterInput = new CharacterInput
        {

            Move      = input.Move.ReadValue<Vector2>(),
            Rotation  = playerCamera.transform.rotation,
            Jump      = input.Jump.IsPressed(),
            FlyToggle = input.FlyToggle.WasPressedThisFrame(),
            FlyDown   = input.FlyDown.IsPressed()

        };

        playerController.UpdateCharacter(characterInput);

    }

    private void LateUpdate()
    {
        
        playerCamera.UpdatePosition(playerController.GetCameraTarget());

    }

}

