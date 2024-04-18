#version 330

layout (location = 0) in vec3 a_position;
layout (location = 1) in vec3 a_normal;
layout (location = 2) in vec2 a_uv;
layout (location = 3) in vec4 a_color;

uniform mat4 u_world;
uniform mat4 u_view;
uniform mat4 u_projection;
uniform vec3 u_cam_position;

out VS_OUT {
    vec3 local_position;
    vec3 local_normal;

    vec3 world_position;
    vec3 world_normal;

    vec3 view_position;
    vec2 uv;

    vec4 vertex_color;
} vs_out;

void main() {
    vs_out.local_position = a_position;
    vs_out.local_normal = a_normal;
    vs_out.uv = vec2(a_uv.x, 1.0 - a_uv.y);

    vec4 world_position = u_world * vec4(a_position.xyz, 1.0);
    vec4 world_normal = u_world * vec4(a_normal.xyz, 0.0);
    vec4 view_position = u_view * world_position;

    vs_out.world_position = world_position.xyz;
    vs_out.world_normal = world_normal.xyz;
    vs_out.view_position = view_position.xyz;

    vs_out.vertex_color = a_color;

    gl_Position = u_projection * view_position;
}