using System.Numerics;
using Silk.NET.OpenGL;

namespace Engine.Client.Graphics;

/// <summary>
///		A compiled shader program: a vertex shader and a fragment shader linked together.
/// </summary>
/// <remarks>
///		OpenGL refers to everything it owns (shaders, buffers, textures) by a plain number.
///		This class holds one and then frees it.
/// </remarks>
public sealed class Shader : IDisposable
{
	private readonly GL _gl;

	public uint Handle { get; }

	/// <exception cref="ShaderException">A shader failed to compile, or the two failed to link.</exception>
	public Shader(GL gl, string vertexSource, string fragmentSource, string name)
	{
		_gl = gl;

		var vertex = Compile(ShaderType.VertexShader, vertexSource, $"{name}.vert");
		var fragment = Compile(ShaderType.FragmentShader, fragmentSource, $"{name}.frag");

		// Linking joins the two into one program, and checks that vertex's output matches the fragment's input.
		// Once linked, the program has its own copy so that the separate shaders can be freed.
		Handle = _gl.CreateProgram();
		_gl.AttachShader(Handle, vertex);
		_gl.AttachShader(Handle, fragment);
		_gl.LinkProgram(Handle);
		
		_gl.DetachShader(Handle, vertex);
		_gl.DetachShader(Handle, fragment);
		_gl.DeleteShader(vertex);
		_gl.DeleteShader(fragment);

		_gl.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out var linked);
		if (linked == 0)
			throw new ShaderException($"Shader '{name}' failed to link: {_gl.GetProgramInfoLog(Handle)}");
	}

	/// <summary>
	///		Loads <c>name.vert</c> and <c>name.frag</c> from a folder.
	/// </summary>
	public static Shader FromFiles(GL gl, string folder, string name)
	{
		return new Shader(gl,
			File.ReadAllText(Path.Combine(folder, $"{name}.vert")),
			File.ReadAllText(Path.Combine(folder, $"{name}.frag")),
			name);
	}

	/// <summary>
	///		Makes this the program the next draw calls use.
	/// </summary>
	public void Use() => _gl.UseProgram(Handle);

	/// <summary>
	///		Sets a <c>uniform mat4</c> in the shader. Uniforms are values that stay the same for a whole draw call,
	///		unlike vertex inputs, which change per corner. Call <see cref="Use"/> first.
	/// </summary>
	/// <exception cref="ShaderException">The shader has no uniform with that name.</exception>
	/// <remarks>
	///		System.Numerics writes matrices row by row and GLSL reads them column by column, but System.Numerics
	///		also multiplies the other way round. The two differences cancel out, so the bytes can be passed as they
	///		are, with transpose = false, and the shader multiplies in the usual GLSL order: projection * view * model.
	/// </remarks>
	public unsafe void SetMatrix4(string name, Matrix4x4 value)
	{
		// The driver removes uniforms the shader never uses, so a typo and an unused uniform both give -1.
		var location = _gl.GetUniformLocation(Handle, name);
		if (location == -1)
			throw new ShaderException($"Uniform '{name}' not found in shader, or not used by it.");

		_gl.UniformMatrix4(location, 1, false, (float*) &value);
	}

	public void Dispose() => _gl.DeleteProgram(Handle);

	private uint Compile(ShaderType type, string source, string fileName)
	{
		var shader = _gl.CreateShader(type);
		_gl.ShaderSource(shader, source);
		_gl.CompileShader(shader);

		// Compiling happens in the graphics driver. If it fails, nothing throws on its own.
		// We have to ask whether it worked, and fetch the driver's error message ourselves.
		_gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);
		if (compiled == 0)
		{
			var log = _gl.GetShaderInfoLog(shader);
			_gl.DeleteShader(shader);
			throw new ShaderException($"{fileName} failed to compile: {log}");
		}

		return shader;
	}
}

public sealed class ShaderException(string message) : Exception(message);
