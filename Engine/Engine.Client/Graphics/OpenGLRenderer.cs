using System.Drawing;
using System.Numerics;
using Silk.NET.OpenGL;

namespace Engine.Client.Graphics;

/// <summary>
///		The OpenGL 3.3 renderer.
/// </summary>
/// <remarks>
///		Drawing anything in OpenGL takes the same four pieces:
///		<list type="number">
///			<item>A VBO (vertex buffer object): the vertex data itself, copied into GPU memory.</item>
///			<item>A VAO (vertex array object): the description of that data's layout ("3 floats of position, then 3 of color").</item>
///			<item>A shader program, which turns that data into pixels.</item>
///			<item>A draw call, which says "draw N vertices now".</item>
///		</list>
///		Getting it on screen in 3D adds three matrices, passed to the shader as uniforms:
///		model (where this object is in the world), view (where the camera is), and projection (perspective).
/// </remarks>
public sealed class OpenGLRenderer : IRenderer
{
	private readonly GL _gl;
	private readonly string _shaderFolder;

	private Shader? _shader;
	private uint _vao;
	private uint _vbo;
	private uint _ebo;

	/// <summary>
	///		Window width ÷ height, for the projection matrix. Updated by Resize.
	/// </summary>
	private float _aspectRatio = 16f / 9f;

	/// <summary>
	///		Seconds since start, to spin the middle cube.
	/// </summary>
	private double _time;

	/// <summary>
	///		A cube one unit across, centred on (0, 0, 0): its 8 corners, each a position (x, y, z) and a color (r, g, b).
	///		Each corner is shared by three faces, which is exactly what the index buffer is for.
	/// </summary>
	private static readonly float[] Vertices =
	[
		//  x      y      z        r     g     b
		-0.5f, -0.5f,  0.5f,    1.0f, 0.0f, 0.0f, // 0: front  bottom left   red
		 0.5f, -0.5f,  0.5f,    0.0f, 1.0f, 0.0f, // 1: front  bottom right  green
		 0.5f,  0.5f,  0.5f,    0.0f, 0.0f, 1.0f, // 2: front  top right     blue
		-0.5f,  0.5f,  0.5f,    1.0f, 1.0f, 0.0f, // 3: front  top left      yellow
		-0.5f, -0.5f, -0.5f,    1.0f, 0.0f, 1.0f, // 4: back   bottom left   magenta
		 0.5f, -0.5f, -0.5f,    0.0f, 1.0f, 1.0f, // 5: back   bottom right  cyan
		 0.5f,  0.5f, -0.5f,    1.0f, 1.0f, 1.0f, // 6: back   top right     white
		-0.5f,  0.5f, -0.5f,    0.2f, 0.2f, 0.2f, // 7: back   top left      dark grey
	];

	/// <summary>
	///		6 faces × 2 triangles × 3 corners = 36 indices, using only the 8 corners above.
	/// </summary>
	private static readonly uint[] Indices =
	[
		0, 1, 2,  2, 3, 0, // front  (+Z)
		5, 4, 7,  7, 6, 5, // back   (-Z)
		4, 0, 3,  3, 7, 4, // left   (-X)
		1, 5, 6,  6, 2, 1, // right  (+X)
		3, 2, 6,  6, 7, 3, // top    (+Y)
		4, 5, 1,  1, 0, 4, // bottom (-Y)
	];

	/// <summary>
	///		Where to draw a copy of the cube. Same mesh, different model matrix: that's how one model gets
	///		drawn in many places, like every tree in a forest.
	/// </summary>
	private static readonly Vector3[] CubePositions =
	[
		new(0, 0, 0),
		new(2, 0, -2),
		new(-2, 0, -2),
		new(0, 2, -4),
		new(0, -1.5f, 0),
	];

	/// <summary>
	///		Floats per vertex: 3 for position + 3 for colour.
	/// </summary>
	private const int FloatsPerVertex = 6;

	public OpenGLRenderer(GL gl, string shaderFolder)
	{
		_gl = gl;
		_shaderFolder = shaderFolder;
	}

	public unsafe void Initialize()
	{
		Console.WriteLine($"OpenGL: {_gl.GetStringS(StringName.Version)} on {_gl.GetStringS(StringName.Renderer)}");

		_shader = Shader.FromFiles(_gl, _shaderFolder, "basic");

		// The VAO must be bound first: while it's bound, it records the buffer and layout set up below.
		// Later, binding just the VAO brings all of that back in one call.
		_vao = _gl.GenVertexArray();
		_gl.BindVertexArray(_vao);

		// Create the buffer, and copy the vertices into it. StaticDraw hints "written once, drawn many times".
		_vbo = _gl.GenBuffer();
		_gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
		_gl.BufferData(BufferTargetARB.ArrayBuffer, Vertices, BufferUsageARB.StaticDraw);

		_ebo = _gl.GenBuffer();
		_gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
		_gl.BufferData(BufferTargetARB.ElementArrayBuffer, Indices, BufferUsageARB.StaticDraw);

		// Stride = bytes from one vertex to the next. The last argument is where each vertex the attribute starts,
		//	in bytes.
		// OpenGL wants that as a pointer, for historical reasons,
		//	which is why this method is "unsafe".
		const uint stride = FloatsPerVertex * sizeof(float);

		// location 0 in basic.vert: aPosition, 3 floats, starting at byte 0.
		_gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*) 0);
		_gl.EnableVertexAttribArray(0);

		// location 1 in basic.vert: aColor, 3 floats, starting after the 3 position floats.
		_gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*) (3 * sizeof(float)));
		_gl.EnableVertexAttribArray(1);

		// Unbind, so later code can't change this VAO by accident.
		_gl.BindVertexArray(0);

		// Depth testing: for every pixel, keep only the nearest thing drawn there.
		// Without it, whatever is drawn LAST wins, so the back of a cube can cover its front.
		_gl.Enable(EnableCap.DepthTest);

		_gl.ClearColor(Color.FromArgb(255, 25, 25, 38));
	}

	public unsafe void Render(double deltaTime, Camera camera)
	{
		_time += deltaTime;

		// Wipe the previous frame: the colours, and the depth of everything drawn (or depth tests use last frame's).
		_gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

		_shader!.Use();

		// View and projection are the same for every object this frame, so set them once.
		_shader.SetMatrix4("uView", camera.ViewMatrix);
		_shader.SetMatrix4("uProjection", camera.ProjectionMatrix(_aspectRatio));

		_gl.BindVertexArray(_vao);

		for (var i = 0; i < CubePositions.Length; i++)
		{
			// The model matrix places this copy in the world. The first cube also spins.
			// System.Numerics applies left to right: rotate first, then move.
			var rotation = i == 0
				? Matrix4x4.CreateRotationY((float) _time) * Matrix4x4.CreateRotationX((float) _time * 0.5f)
				: Matrix4x4.Identity;

			_shader.SetMatrix4("uModel", rotation * Matrix4x4.CreateTranslation(CubePositions[i]));
			_gl.DrawElements(PrimitiveType.Triangles, (uint) Indices.Length, DrawElementsType.UnsignedInt, (void*) 0);
		}
	}

	public void Resize(int width, int height)
	{
		// Tell OpenGL that the drawing area changed.
		// Without this, the picture stays as the old size.
		_gl.Viewport(0, 0, (uint) width, (uint) height);

		// A minimized window reports 0 height; dividing by it would break the projection.
		if (height > 0)
			_aspectRatio = (float) width / height;
	}

	/// <summary>
	///		Frees OpenGL memory by hand, since C# doesn't handle it.
	/// </summary>
	public void Dispose()
	{
		_gl.DeleteBuffer(_vbo);
		_gl.DeleteBuffer(_ebo);
		_gl.DeleteVertexArray(_vao);
		_shader?.Dispose();
	}
}
