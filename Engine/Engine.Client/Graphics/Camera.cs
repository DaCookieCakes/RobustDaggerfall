using System.Numerics;

namespace Engine.Client.Graphics;

/// <summary>
///		Where the world is seen from, and how: a position, a direction, and a field of view.
/// </summary>
/// <remarks>
///		This is OpenGL's usual convention. Daggerfall's own models use a different axis,
///		so the model converter will have to turn them into these.
/// </remarks>
public sealed class Camera
{
	public Vector3 Position { get; set; } = new(0, 0, 3);

	/// <summary>
	///		Turning left and right, in degrees. 0 looks along -Z; positive turns right.
	/// </summary>
	public float Yaw { get; set; }

	/// <summary>
	///		Looking up and down, in degrees. Kept between -89 and 89: at exactly ±90 "up" and "forward" would
	///		point the same way, and the view matrix can't be worked out.
	/// </summary>
	public float Pitch
	{
		get => _pitch;
		set => _pitch = Math.Clamp(value, -89f, 89f);
	}

	private float _pitch;

	/// <summary>
	///		How wide the view is, top to bottom, in degrees. Bigger sees more but looks more stretched at the edges.
	/// </summary>
	public float FieldOfView { get; set; } = 70f;

	/// <summary>
	///		Anything closer than this, or further than <see cref="FarPlane"/>, isn't drawn.
	///		Keep the ratio between them modest: the depth buffer gets less precise the bigger it is.
	/// </summary>
	public float NearPlane { get; set; } = 0.1f;

	public float FarPlane { get; set; } = 1000f;

	/// <summary>
	///		The direction the camera is looking, one unit long.
	/// </summary>
	/// <remarks>
	///		This is the trigonometry for "a point on a sphere": yaw spins around the Y axis, pitch tilts up from flat.
	///		With yaw and pitch at 0, it gives (0, 0, -1): straight along -Z.
	/// </remarks>
	public Vector3 Forward
	{
		get
		{
			var yaw = float.DegreesToRadians(Yaw);
			var pitch = float.DegreesToRadians(Pitch);

			return Vector3.Normalize(new Vector3(
				MathF.Sin(yaw) * MathF.Cos(pitch),
				MathF.Sin(pitch),
				-MathF.Cos(yaw) * MathF.Cos(pitch)));
		}
	}

	/// <summary>
	///		The camera's right, flat along the ground. Used for strafing.
	/// </summary>
	public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitY));

	/// <summary>
	///		The view matrix moves the whole world so the camera sits at the origin looking down -Z.
	///		OpenGL has no camera of its own: "moving the camera right" is really "moving everything else left".
	/// </summary>
	public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Position + Forward, Vector3.UnitY);

	/// <summary>
	///		The projection matrix adds perspective: far things shrink towards the centre.
	///		It also squeezes the view into -1..1, which is the only space OpenGL draws.
	/// </summary>
	/// <param name="aspectRatio">Window width ÷ height. This is what stops shapes stretching with the window.</param>
	public Matrix4x4 ProjectionMatrix(float aspectRatio)
	{
		return Matrix4x4.CreatePerspectiveFieldOfView(
			float.DegreesToRadians(FieldOfView), aspectRatio, NearPlane, FarPlane);
	}
}
