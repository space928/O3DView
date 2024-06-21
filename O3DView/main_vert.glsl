#version 430 core
layout (location = 0) in vec3 pos;
layout (location = 1) in vec3 nrm;
layout (location = 2) in vec2 uv;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;

out vec2 vUv;
out vec3 vNrm;
out vec3 vTan;
out vec3 vBin;
out vec3 vPos;
//out vec3 vViewNrm;

void main()
{
    vec4 worldPos = uModel * vec4(pos, 1.0);
    vec4 viewPos = uView * worldPos;
    gl_Position = uProj * viewPos;
    vUv = uv;
    vNrm = nrm;
    vTan = normalize(cross(nrm, vec3(0,1.,0.)));
    vBin = cross(nrm, vTan);
    vPos = worldPos.xyz;
    //vViewNrm = (uView * vec4(nrm, 0.)).xyz;
}
