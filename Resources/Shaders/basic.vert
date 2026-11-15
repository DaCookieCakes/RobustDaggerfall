// Vertex shader: runs once per corner (vertex). Its job is to say where that corner goes on screen.
#version 330 core

// Inputs, one set per vertex. "location" numbers must match VertexAttribPointer's first argument in Renderer.cs.
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aColor;

// Passed on to the fragment shader. The GPU blends it smoothly across the triangle.
out vec3 vColor;

void main()
{
    // gl_Position is the shader's real output: where the corner lands. -1..1 on each axis covers the window.
    gl_Position = vec4(aPosition, 1.0);
    vColor = aColor;
}
