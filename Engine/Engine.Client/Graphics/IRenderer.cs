namespace Engine.Client.Graphics;

/// <summary>
///		Draws the game. The rest of the engine talks to this, never to OpenGL directly.
/// </summary>
/// <remarks>
///		This bkeeps OpenGL behind an interface means a headless version, 
///		so one that does nothing can stand in where there's no window, such as on a server or in tests.
/// </remarks>
public interface IRenderer : IDisposable
{
	/// <summary>
	///		Creates GPU resources (shaders and buffers). Called after the window exists.
	/// </summary>
	void Initialize();

	/// <summary>
	///		Draws one frame.
	/// </summary>
	/// <param name="deltaTime">Seconds since the last frame.</param>
	void Render(double deltaTime);

	/// <summary>
	///		Called when the window changes size, in pixels.
	/// </summary>
	void Resize(int width, int height);
}
