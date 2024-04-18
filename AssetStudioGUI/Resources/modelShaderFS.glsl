#version 330

in VS_OUT {
    vec3 local_position;
    vec3 local_normal;

    vec3 world_position;
    vec3 world_normal;

    vec3 view_position;
    vec2 uv;

    vec4 vertex_color;
} fs_in;

uniform sampler2D u_diffuse_map;
uniform vec3 u_cam_position;
uniform bool u_enable_diffuse_map;

out vec4 frag_color;

vec4 gamma_correct(vec4 color, float gamma) {
    vec4 out_color = color;
    out_color.xyz = pow (out_color.xyz, vec3 (1.0 / gamma));

    return out_color;
}

void main() {
    const float shininess = 10.0;
    const float intensity = 0.75;
    const float gamma = 0.85;

    const vec3 light_color = vec3(0.8, 0.8, 0.8);
    const vec3 ambient = vec3(0.3, 0.3, 0.3);

    vec3 light_dir = normalize(u_cam_position - fs_in.world_position);
    vec3 viewer_dir = light_dir;
    vec3 normal = fs_in.world_normal;
    
    vec3 diffuse = vec3(0.0);
    vec3 specular = vec3(0.0);

    float diff_factor = max(dot(normal, light_dir), 0.0);
    diffuse = intensity * diff_factor * light_color;

    vec3 reflected = normalize(reflect(-light_dir, normal));
    float spec_factor = max(dot(viewer_dir, reflected), 0.0);
    specular = intensity * pow(spec_factor, shininess) * light_color;

    float dist = length(u_cam_position - fs_in.world_position);
    vec3 phong_component = specular + diffuse / (dist * dist);

    if (u_enable_diffuse_map) {
        vec3 tex_color = texture(u_diffuse_map, fs_in.uv).xyz;
        frag_color = gamma_correct(vec4(tex_color * (ambient + diffuse) + specular, 1.0), gamma);
    } else {
        frag_color = gamma_correct(vec4(ambient + specular + diffuse, 1.0), gamma);
    }
}