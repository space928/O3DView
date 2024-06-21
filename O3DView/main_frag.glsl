#version 430 core
in vec2 vUv;
in vec3 vNrm;
in vec3 vTan;
in vec3 vBin;
in vec3 vPos;
//in vec3 vViewNrm;

out vec4 FragColor;

// O3D Material
uniform sampler2D uMainTex;
uniform float uUseMainTex;
uniform vec4 uAlbedo;
uniform vec3 uSpec;
uniform vec3 uEmission;
uniform float uRough;

// CFG Material
uniform sampler2D uTransmap;
uniform float uUseTransmap;
uniform sampler2D uNightmap;
uniform float uUseNightmap;
uniform sampler2D uLightmap;
uniform float uUseLightmap;
uniform sampler2D uEnvmap;
uniform float uUseEnvmap;
uniform float uEnvmapStrength;
uniform sampler2D uBumpmap;
uniform float uUseBumpmap;
uniform float uBumpmapStrength;

// Renderer
uniform float uSelected;
uniform vec3 uViewDir;
uniform vec3 uViewPos;
uniform float uTime;
uniform float uFullbright;
uniform float uWireframe;
uniform float uCutout;
uniform float uLightmapStrength;
uniform float uNightmapStrength;

struct light_data
{
    int mode;
    vec3 col;
    float intensity;
    vec3 dir;
    vec3 pos;
    vec3 amb;
    float range;
};

layout(std430, binding = 2) readonly buffer light_ssbo
{
    light_data lights[];
};

vec3 shade_lights(vec3 diffuse, vec3 spec, float rough, vec3 nrm, vec3 viewDir)
{
    vec3 col = vec3(0.);
    for(int l = 0; l < lights.length(); l++)
    {
        light_data light = lights[l];
        switch(light.mode)
        {
            case 0:
            {
                // Ambient only
                col += diffuse * light.amb * light.intensity;
                break;
            }
            case 1:
            {
                // Directional
                vec3 diff = light.amb * light.intensity;
                vec3 lightDir = normalize(-light.dir);
                float ndotl = dot(lightDir, nrm);
                diff += (ndotl * .5 + .5) * light.intensity * light.col;

                vec3 halfDir = normalize(lightDir - viewDir);
                float specAngle = max(dot(halfDir, nrm), 0.0);
                vec3 specCol = pow(max(specAngle, 1e-6), 1./rough-1.) * spec * light.intensity * light.col;
                
                col += diffuse * diff + specCol;
                break;
            }
            case 2:
            {
                // Point
                vec3 diff = light.amb * light.intensity;

                vec3 ldir = light.pos - vPos;
                float distSqr = dot(ldir, ldir);
                float dist = sqrt(distSqr);
                ldir = ldir / dist;
                if (dist > light.range * 10.) 
                    break;
                float atten = light.range/(distSqr+1.); // ~ Inverse square law

                float ndotl = dot(ldir, nrm);
                diff += max(ndotl, 0.) * light.intensity * light.col;

                vec3 halfDir = normalize(ldir - viewDir);
                float specAngle = max(dot(halfDir, nrm) * ndotl, 0.0);
                vec3 specCol = pow(max(specAngle, 1e-6), 1./rough-1.) * spec * light.intensity * light.col;

                col += (diffuse * diff + specCol) * atten;
                break;
            }
            case 3:
            {
                // Spot
                break;
            } 
            default:
                break;
        }
    }
    return col;
}

void main()
{
    vec4 col = uAlbedo;
    vec4 mainTex = vec4(1.);
    vec2 uv = vUv;
    if(uUseMainTex > 0) 
    {
        mainTex = texture(uMainTex, uv);
        col.rgb *= mainTex.rgb;
        col.a = mainTex.a;
    }
    if(uUseTransmap > 0)
    {
        vec4 transmap = texture(uTransmap, uv);
        col.a = transmap.r;
    }

    if(uCutout > 0)
        if(col.a < 0.5)
            discard;
    
    vec3 nrm = vNrm;
    vec3 viewDir = normalize(vPos - uViewPos);
    
    if(uUseBumpmap > 0) 
    {
        float bump00 = texture(uBumpmap, uv+.02*vec2(-1,0.)).r;
        float bump01 = texture(uBumpmap, uv+.02*vec2(0.,-1)).r;
        float bump10 = texture(uBumpmap, uv+.02*vec2(1.,0.)).r;
        float bump11 = texture(uBumpmap, uv+.02*vec2(0.,1.)).r;
        vec2 db = vec2(bump00-bump01, bump10-bump11) * uBumpmapStrength;
        //vec3 nt = vec3(dFdx(bump1), dFdy(bump1), 0.);
        vec3 nt = vec3(db, 1.-length(db));

        vec3 bump = vTan * nt.x + vBin * nt.y + vNrm * nt.z;
        nrm = bump;
        //reflNrm.xy += nt.xy;
    }
    
    if(uFullbright == 0 && uWireframe == 0)
        col.rgb = shade_lights(col.rgb, uSpec, uRough, nrm, viewDir);

    if(uWireframe > 0)
        col.a = 1.;

    if(uUseEnvmap > 0 && uEnvmapStrength > 0)
    {
        //vec3 env = texture(uEnvmap, reflect(viewDir, nrm).xy*.5+.5).rgb;
        vec3 env = texture(uEnvmap, clamp((viewDir + nrm).xy*.5+.5, .01, .99)).rgb;
        col.rgb = mix(col.rgb, env, clamp(col.a * uEnvmapStrength, 0., 1.));
        //col.rgb += env * col.a * uEnvmapStrength;
    }

    col.rgb += uEmission * mainTex.rgb;

    if(uUseNightmap > 0 && uNightmapStrength > 0)
        col.rgb += texture(uNightmap, uv).rgb * uNightmapStrength;

    if(uUseLightmap > 0 && uLightmapStrength > 0)
        col.rgb += texture(uLightmap, uv).rgb * uLightmapStrength;

    if(uSelected > 0)
        col.rgb += vec3(0.8, 0.8, 0.3) * (sin(uTime*3.14)*.5+.5);// * pow(clamp(1.-dot(uViewDir, vNrm), 0., 1.), 2.);

    //col.rgb = nrm;

    FragColor = col;
}
