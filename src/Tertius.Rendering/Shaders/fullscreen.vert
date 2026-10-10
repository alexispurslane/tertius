#version 460

// One triangle that covers the screen: (-1,-1), (3,-1), (-1,3). Used by passes that only need gl_FragCoord.
void main()
{
    vec2 ndc = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2) * 2.0 - 1.0;
    gl_Position = vec4(ndc, 0.0, 1.0);
}
