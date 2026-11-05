#ifndef TERTIUS_ENVIRONMENT_BOX_GLSL
#define TERTIUS_ENVIRONMENT_BOX_GLSL

bool finiteBoxVector(vec3 value)
{
    return !any(isnan(value)) && !any(isinf(value));
}

float largestBoxComponent(vec3 value)
{
    return max(abs(value.x), max(abs(value.y), abs(value.z)));
}

vec3 boxProjectedDirection(vec3 minimum, vec3 maximum, vec3 position, vec3 direction)
{
    if (!finiteBoxVector(direction) || largestBoxComponent(direction) < 1e-20)
        return vec3(0.0, 0.0, 1.0);
    if (!finiteBoxVector(minimum) || !finiteBoxVector(maximum) || !finiteBoxVector(position))
        return direction;
    if (!all(lessThan(minimum, maximum)))
        return direction;

    float scale = max(largestBoxComponent(position), max(largestBoxComponent(minimum), largestBoxComponent(maximum)));
    if (scale < 1e-20)
        return direction;
    minimum /= scale;
    maximum /= scale;
    position /= scale;
    if (!all(lessThan(minimum, maximum)))
        return direction;
    vec3 along = direction / largestBoxComponent(direction);
    float entry = -3.402823e38;
    float exit = 3.402823e38;
    for (int axis = 0; axis < 3; axis++)
    {
        if (abs(along[axis]) < 1e-20)
        {
            if (position[axis] < minimum[axis] || position[axis] > maximum[axis])
                return direction;
        }
        else
        {
            float first = (minimum[axis] - position[axis]) / along[axis];
            float last = (maximum[axis] - position[axis]) / along[axis];
            entry = max(entry, min(first, last));
            exit = min(exit, max(first, last));
        }
    }
    if (exit < max(entry, 0.0))
        return direction;
    float distance = entry >= 0.0 ? entry : exit;
    precise vec3 lookup = position + distance * along;
    if (!finiteBoxVector(lookup) || largestBoxComponent(lookup) < 1e-20)
        return direction;
    return lookup;
}

#endif
