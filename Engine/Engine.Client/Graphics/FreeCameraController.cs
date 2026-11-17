using System.Numerics;
using Silk.NET.Input;

namespace Engine.Client.Graphics;

/// <summary>
///		A free-flying debug camera.
/// </summary>
/// <remarks>
///		This is a developer tool, not the player's camera. The player's camera will follow their entity instead.
/// </remarks>
public sealed class FreeCameraController
{
	private readonly Camera _camera;
	private readonly IKeyboard _keyboard;
	private readonly IMouse _mouse;

	/// <summary>
	///		Movement speed in world units per second.
	/// </summary>
	public float Speed { get; set; } = 7f;

	/// <summary>
	///		Degrees turned per pixel the mouse moves.
	/// </summary>
	public float Sensitivity { get; set; } = 0.1f;

	/// <summary>
	///		Where the mouse was last time it moved, or null before the first move.
	/// </summary>
	private Vector2? _lastMouse;

	/// <summary>
	///		Whether the mouse is in 'NormalMode'. This is just to lock the last position.
	/// </summary>
	private bool _normalMode;

	public FreeCameraController(Camera camera, IKeyboard keyboard, IMouse mouse)
	{
		_camera = camera;
		_keyboard = keyboard;
		_mouse = mouse;
		
		_mouse.Cursor.CursorMode = CursorMode.Raw;
		_mouse.MouseMove += OnMouseMove;

		_keyboard.KeyDown += OnKeyDown;
	}

	/// <summary>
	///		Moves the camera for keys currently held. Call once per frame.
	/// </summary>
	/// <param name="deltaTime">Seconds since the last frame. Multiplying by this makes speed the same at any frame rate.</param>
	public void Update(double deltaTime)
	{
		var move = Vector3.Zero;
		
		if (_keyboard.IsKeyPressed(Key.W)) move += _camera.Forward;
		if (_keyboard.IsKeyPressed(Key.S)) move -= _camera.Forward;
		if (_keyboard.IsKeyPressed(Key.D)) move += _camera.Right;
		if (_keyboard.IsKeyPressed(Key.A)) move -= _camera.Right;
		if (_keyboard.IsKeyPressed(Key.Space)) move += Vector3.UnitY;
		if (_keyboard.IsKeyPressed(Key.ControlLeft)) move -= Vector3.UnitY;

		if (move == Vector3.Zero)
			return;

		// Normalize movement so that holding W and D together isn't faster than W alone.
		var speed = _keyboard.IsKeyPressed(Key.ShiftLeft) ? Speed * 4 : Speed;
		_camera.Position += Vector3.Normalize(move) * speed * (float) deltaTime;
	}

	private void OnMouseMove(IMouse mouse, Vector2 position)
	{
		// The first event only tells us where the mouse starts. Turning by that would make the view jump.
		// Also, screen Y grows downwards,
		//	so moving the mouse up gives a negative delta.
		if (!_normalMode && _lastMouse is { } last)
		{
			var delta = position - last;
			_camera.Yaw += delta.X * Sensitivity;
			_camera.Pitch -= delta.Y * Sensitivity;
		}

		_lastMouse = position;
	}
	
	/// <summary>
	///             Tab toggles the mouse lock: locked for looking around, unlocked to use the cursor.
	/// </summary>
	private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
	{
		if (key != Key.Tab)
			return;

		_normalMode = !_normalMode;
		_mouse.Cursor.CursorMode = _normalMode ? CursorMode.Normal : CursorMode.Raw;

		// Forget the old position, so re-locking doesn't turn the view.
		_lastMouse = null;
	}
}
