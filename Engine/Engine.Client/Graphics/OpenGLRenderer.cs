using System.Drawing;
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
	///		The triangle: three corners, each a position (x, y, z) followed by a color (r, g, b).
	///		Positions run from -1 to 1 across the window: (0, 0) is the center, (-1, -1) the bottom-left.
	/// </summary>
	private static readonly float[] Vertices =
	[ 
		//  x	   y	  z		   r	  g		 b
		 0.5f,	0.5f,  0.0f,    1.0f,  0.0f,  0.0f,	// top-right
		 0.5f, -0.5f,  0.0f,    0.0f,  1.0f,  0.0f,	// bottom-right
		-0.5f, -0.5f,  0.0f,    0.0f,  0.0f,  1.0f,	// bottom-left
		-0.5f,  0.5f,  0.0f,    1.0f,  0.0f,  1.0f,	// top-left
	];

	private static readonly uint[] Indices =
	[
		0, 1, 3, // First tri
		1, 2, 3	 // Second tri
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

		_gl.ClearColor(Color.FromArgb(255, 25, 25, 38));
	}

	public unsafe void Render(double deltaTime)
	{
		// Wipe the previous frame.
		_gl.Clear(ClearBufferMask.ColorBufferBit);

		_shader!.Use();
		_gl.BindVertexArray(_vao);
		
		_gl.DrawElements(PrimitiveType.Triangles, (uint) Indices.Length, DrawElementsType.UnsignedInt, (void*) 0);
	}

	public void Resize(int width, int height)
	{
		// Tell OpenGL that the drawing area changed.
		// Without this, the picture stays as the old size.
		_gl.Viewport(0, 0, (uint) width, (uint) height);
	}

	public void Dispose()
	{
		// GPU memory isn't freed by C#'s garbage collector,
		//	so we release it by hand.
		_gl.DeleteBuffer(_vbo);
		_gl.DeleteVertexArray(_vao);
		_shader?.Dispose();
	}
}
