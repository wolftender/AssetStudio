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

uniform sampler2D u_diffuse;
uniform vec4 u_surface_color;

out vec4 frag_color;

void main() {
    frag_color = vec4(1.0, 0.0, 0.0, 1.0);
}