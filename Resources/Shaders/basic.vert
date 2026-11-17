// Vertex shader: runs once per corner (vertex). Its job is to say where that corner goes on screen.
#version 330 core

// Inputs, one set per vertex. "location" numbers must match VertexAttribPointer's first argument in OpenGLRenderer.cs.
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aColor;

// Uniforms: the same for every corner in one draw call. Set with Shader.SetMatrix4.
uniform mat4 uModel;      // The object's place in the world: model space → world space.
uniform mat4 uView;       // The camera: world space → camera space.
uniform mat4 uProjection; // Perspective: camera space → the -1..1 box OpenGL draws.

// Passed on to the fragment shader. The GPU blends it smoothly across each triangle.
out vec3 vColor;

void main()
{
    // Read right to left: place the corner in the world, then view it from the camera, then add perspective.
    gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
    vColor = aColor;
}
