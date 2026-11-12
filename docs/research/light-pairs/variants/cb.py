# The working tree with main's lighting.glsl: the cascade bounds without the floor's shader code.
import shutil, sys
shutil.copyfile('/private/tmp/t2-mlvy/main-lighting.glsl', sys.argv[1] + '/src/Tertius.Rendering/Shaders/lighting.glsl')
