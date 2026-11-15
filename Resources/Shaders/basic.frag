// Fragment shader: runs once per pixel the triangle covers. Its job is to say what colour that pixel is.
#version 330 core

// From the vertex shader, already blended between the three corners for this pixel.
in vec3 vColor;

out vec4 FragColor;

void main()
{
    // The last number is alpha (opacity): 1 = solid.
    FragColor = vec4(vColor, 1.0);
}
