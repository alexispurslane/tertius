// Writes a timestamp before a sequence of passes and one after each pass, in one command buffer, and prints the stamps.
// A pass is a compute dispatch that loops (C) or a render pass that only clears a 2048 x 2048 image (R). A global barrier
// sits between two passes, as in a renderer. Every stamp is vkCmdWriteTimestamp2 at VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,
// outside any render pass instance, in the same submission, so no stamp may read lower than the one before it.
//
// Build and run in the Tertius dev shell, in this folder:
//   glslc -O -mfmt=num -o busy.inc busy.comp
//   cc -O1 -o timestamps timestamps.c -lvulkan
//   VK_ICD_FILENAMES=$TERTIUS_KOSMICKRISP_ICD ./timestamps CCRCCR 3
// The first argument is the passes, the second the number of submissions.
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <vulkan/vulkan.h>

static const uint32_t busy_spirv[] = {
#include "busy.inc"
};

#define CHECK(call)                                                                                                    \
    do {                                                                                                               \
        VkResult r_ = (call);                                                                                          \
        if (r_ != VK_SUCCESS) {                                                                                        \
            fprintf(stderr, "%s failed: %d\n", #call, r_);                                                            \
            exit(1);                                                                                                   \
        }                                                                                                              \
    } while (0)

static uint32_t memory_type(VkPhysicalDevice gpu, uint32_t bits, VkMemoryPropertyFlags wanted)
{
    VkPhysicalDeviceMemoryProperties props;
    vkGetPhysicalDeviceMemoryProperties(gpu, &props);
    for (uint32_t i = 0; i < props.memoryTypeCount; i++)
        if ((bits & (1u << i)) && (props.memoryTypes[i].propertyFlags & wanted) == wanted)
            return i;
    fprintf(stderr, "no memory type\n");
    exit(1);
}

static int has_extension(const VkExtensionProperties *list, uint32_t count, const char *name)
{
    for (uint32_t i = 0; i < count; i++)
        if (strcmp(list[i].extensionName, name) == 0)
            return 1;
    return 0;
}

static void barrier(VkCommandBuffer cmd)
{
    VkMemoryBarrier2 memory = {
        .sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER_2,
        .srcStageMask = VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,
        .srcAccessMask = VK_ACCESS_2_MEMORY_WRITE_BIT,
        .dstStageMask = VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,
        .dstAccessMask = VK_ACCESS_2_MEMORY_READ_BIT | VK_ACCESS_2_MEMORY_WRITE_BIT,
    };
    VkDependencyInfo info = {
        .sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO, .memoryBarrierCount = 1, .pMemoryBarriers = &memory};
    vkCmdPipelineBarrier2(cmd, &info);
}

int main(int argc, char **argv)
{
    const char *passes = argc > 1 ? argv[1] : "CCRCCR";
    int submissions = argc > 2 ? atoi(argv[2]) : 3;
    uint32_t iterations = argc > 3 ? (uint32_t)atoi(argv[3]) : 20000;
    uint32_t pass_count = (uint32_t)strlen(passes);
    uint32_t stamps = pass_count + 1;

    uint32_t ext_count = 0;
    vkEnumerateInstanceExtensionProperties(NULL, &ext_count, NULL);
    VkExtensionProperties *exts = calloc(ext_count, sizeof *exts);
    vkEnumerateInstanceExtensionProperties(NULL, &ext_count, exts);
    int portability = has_extension(exts, ext_count, VK_KHR_PORTABILITY_ENUMERATION_EXTENSION_NAME);
    const char *instance_ext = VK_KHR_PORTABILITY_ENUMERATION_EXTENSION_NAME;
    VkApplicationInfo app = {.sType = VK_STRUCTURE_TYPE_APPLICATION_INFO, .apiVersion = VK_API_VERSION_1_3};
    VkInstanceCreateInfo instance_info = {
        .sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO,
        .flags = portability ? VK_INSTANCE_CREATE_ENUMERATE_PORTABILITY_BIT_KHR : 0,
        .pApplicationInfo = &app,
        .enabledExtensionCount = portability ? 1 : 0,
        .ppEnabledExtensionNames = &instance_ext,
    };
    VkInstance instance;
    CHECK(vkCreateInstance(&instance_info, NULL, &instance));

    uint32_t gpu_count = 1;
    VkPhysicalDevice gpu;
    vkEnumeratePhysicalDevices(instance, &gpu_count, &gpu);
    VkPhysicalDeviceDriverProperties driver = {.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DRIVER_PROPERTIES};
    VkPhysicalDeviceProperties2 props = {.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2, .pNext = &driver};
    vkGetPhysicalDeviceProperties2(gpu, &props);
    float period = props.properties.limits.timestampPeriod;

    uint32_t family_count = 0;
    vkGetPhysicalDeviceQueueFamilyProperties(gpu, &family_count, NULL);
    VkQueueFamilyProperties *families = calloc(family_count, sizeof *families);
    vkGetPhysicalDeviceQueueFamilyProperties(gpu, &family_count, families);
    uint32_t family = 0;
    while (family < family_count &&
           !((families[family].queueFlags & VK_QUEUE_GRAPHICS_BIT) && (families[family].queueFlags & VK_QUEUE_COMPUTE_BIT)))
        family++;
    printf("%s, %s %s: timestampPeriod %.3f ns, timestampValidBits %u\n", props.properties.deviceName,
           driver.driverName, driver.driverInfo, period, families[family].timestampValidBits);

    uint32_t dev_ext_count = 0;
    vkEnumerateDeviceExtensionProperties(gpu, NULL, &dev_ext_count, NULL);
    VkExtensionProperties *dev_exts = calloc(dev_ext_count, sizeof *dev_exts);
    vkEnumerateDeviceExtensionProperties(gpu, NULL, &dev_ext_count, dev_exts);
    const char *subset = "VK_KHR_portability_subset";
    int has_subset = has_extension(dev_exts, dev_ext_count, subset);
    float priority = 1;
    VkDeviceQueueCreateInfo queue_info = {.sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO,
                                          .queueFamilyIndex = family,
                                          .queueCount = 1,
                                          .pQueuePriorities = &priority};
    VkPhysicalDeviceVulkan13Features features13 = {.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES,
                                                   .synchronization2 = VK_TRUE,
                                                   .dynamicRendering = VK_TRUE};
    VkDeviceCreateInfo device_info = {.sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO,
                                      .pNext = &features13,
                                      .queueCreateInfoCount = 1,
                                      .pQueueCreateInfos = &queue_info,
                                      .enabledExtensionCount = has_subset ? 1 : 0,
                                      .ppEnabledExtensionNames = &subset};
    VkDevice device;
    CHECK(vkCreateDevice(gpu, &device_info, NULL, &device));
    VkQueue queue;
    vkGetDeviceQueue(device, family, 0, &queue);

    // The buffer that the compute pass writes.
    uint32_t invocations = 64 * 1024;
    VkBufferCreateInfo buffer_info = {.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO,
                                      .size = invocations * sizeof(uint32_t),
                                      .usage = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT};
    VkBuffer buffer;
    CHECK(vkCreateBuffer(device, &buffer_info, NULL, &buffer));
    VkMemoryRequirements buffer_needs;
    vkGetBufferMemoryRequirements(device, buffer, &buffer_needs);
    VkMemoryAllocateInfo buffer_alloc = {
        .sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
        .allocationSize = buffer_needs.size,
        .memoryTypeIndex = memory_type(gpu, buffer_needs.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT)};
    VkDeviceMemory buffer_memory;
    CHECK(vkAllocateMemory(device, &buffer_alloc, NULL, &buffer_memory));
    CHECK(vkBindBufferMemory(device, buffer, buffer_memory, 0));

    // The image that the render pass clears.
    VkImageCreateInfo image_info = {.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO,
                                    .imageType = VK_IMAGE_TYPE_2D,
                                    .format = VK_FORMAT_R8G8B8A8_UNORM,
                                    .extent = {2048, 2048, 1},
                                    .mipLevels = 1,
                                    .arrayLayers = 1,
                                    .samples = VK_SAMPLE_COUNT_1_BIT,
                                    .tiling = VK_IMAGE_TILING_OPTIMAL,
                                    .usage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT};
    VkImage image;
    CHECK(vkCreateImage(device, &image_info, NULL, &image));
    VkMemoryRequirements image_needs;
    vkGetImageMemoryRequirements(device, image, &image_needs);
    VkMemoryAllocateInfo image_alloc = {
        .sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
        .allocationSize = image_needs.size,
        .memoryTypeIndex = memory_type(gpu, image_needs.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT)};
    VkDeviceMemory image_memory;
    CHECK(vkAllocateMemory(device, &image_alloc, NULL, &image_memory));
    CHECK(vkBindImageMemory(device, image, image_memory, 0));
    VkImageViewCreateInfo view_info = {.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO,
                                       .image = image,
                                       .viewType = VK_IMAGE_VIEW_TYPE_2D,
                                       .format = VK_FORMAT_R8G8B8A8_UNORM,
                                       .subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1}};
    VkImageView view;
    CHECK(vkCreateImageView(device, &view_info, NULL, &view));

    // The compute pipeline.
    VkDescriptorSetLayoutBinding binding = {0, VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, 1, VK_SHADER_STAGE_COMPUTE_BIT, NULL};
    VkDescriptorSetLayoutCreateInfo set_layout_info = {
        .sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO, .bindingCount = 1, .pBindings = &binding};
    VkDescriptorSetLayout set_layout;
    CHECK(vkCreateDescriptorSetLayout(device, &set_layout_info, NULL, &set_layout));
    VkPushConstantRange push = {VK_SHADER_STAGE_COMPUTE_BIT, 0, sizeof(uint32_t)};
    VkPipelineLayoutCreateInfo layout_info = {.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO,
                                              .setLayoutCount = 1,
                                              .pSetLayouts = &set_layout,
                                              .pushConstantRangeCount = 1,
                                              .pPushConstantRanges = &push};
    VkPipelineLayout layout;
    CHECK(vkCreatePipelineLayout(device, &layout_info, NULL, &layout));
    VkShaderModuleCreateInfo module_info = {
        .sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO, .codeSize = sizeof busy_spirv, .pCode = busy_spirv};
    VkShaderModule module;
    CHECK(vkCreateShaderModule(device, &module_info, NULL, &module));
    VkComputePipelineCreateInfo pipeline_info = {
        .sType = VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO,
        .stage = {.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,
                  .stage = VK_SHADER_STAGE_COMPUTE_BIT,
                  .module = module,
                  .pName = "main"},
        .layout = layout};
    VkPipeline pipeline;
    CHECK(vkCreateComputePipelines(device, VK_NULL_HANDLE, 1, &pipeline_info, NULL, &pipeline));
    VkDescriptorPoolSize pool_size = {VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, 1};
    VkDescriptorPoolCreateInfo pool_info = {.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO,
                                            .maxSets = 1,
                                            .poolSizeCount = 1,
                                            .pPoolSizes = &pool_size};
    VkDescriptorPool descriptor_pool;
    CHECK(vkCreateDescriptorPool(device, &pool_info, NULL, &descriptor_pool));
    VkDescriptorSetAllocateInfo set_info = {.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO,
                                            .descriptorPool = descriptor_pool,
                                            .descriptorSetCount = 1,
                                            .pSetLayouts = &set_layout};
    VkDescriptorSet set;
    CHECK(vkAllocateDescriptorSets(device, &set_info, &set));
    VkDescriptorBufferInfo set_buffer = {buffer, 0, VK_WHOLE_SIZE};
    VkWriteDescriptorSet write = {.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET,
                                  .dstSet = set,
                                  .descriptorCount = 1,
                                  .descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,
                                  .pBufferInfo = &set_buffer};
    vkUpdateDescriptorSets(device, 1, &write, 0, NULL);

    VkQueryPoolCreateInfo query_info = {
        .sType = VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO, .queryType = VK_QUERY_TYPE_TIMESTAMP, .queryCount = stamps};
    VkQueryPool queries;
    CHECK(vkCreateQueryPool(device, &query_info, NULL, &queries));

    VkCommandPoolCreateInfo command_pool_info = {.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO,
                                                 .flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT,
                                                 .queueFamilyIndex = family};
    VkCommandPool command_pool;
    CHECK(vkCreateCommandPool(device, &command_pool_info, NULL, &command_pool));
    VkCommandBufferAllocateInfo command_info = {.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO,
                                                .commandPool = command_pool,
                                                .level = VK_COMMAND_BUFFER_LEVEL_PRIMARY,
                                                .commandBufferCount = 1};
    VkCommandBuffer cmd;
    CHECK(vkAllocateCommandBuffers(device, &command_info, &cmd));
    VkFenceCreateInfo fence_info = {.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO};
    VkFence fence;
    CHECK(vkCreateFence(device, &fence_info, NULL, &fence));

    uint64_t *results = calloc(2 * stamps, sizeof *results);
    int faults = 0;
    for (int submission = 0; submission < submissions; submission++) {
        VkCommandBufferBeginInfo begin = {.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO,
                                          .flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT};
        CHECK(vkBeginCommandBuffer(cmd, &begin));
        vkCmdResetQueryPool(cmd, queries, 0, stamps);
        VkImageMemoryBarrier2 to_attachment = {.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2,
                                               .srcStageMask = VK_PIPELINE_STAGE_2_NONE,
                                               .dstStageMask = VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,
                                               .dstAccessMask = VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT,
                                               .oldLayout = VK_IMAGE_LAYOUT_UNDEFINED,
                                               .newLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,
                                               .image = image,
                                               .subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1}};
        VkDependencyInfo layout_change = {.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO,
                                          .imageMemoryBarrierCount = 1,
                                          .pImageMemoryBarriers = &to_attachment};
        vkCmdPipelineBarrier2(cmd, &layout_change);
        vkCmdWriteTimestamp2(cmd, VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT, queries, 0);
        for (uint32_t pass = 0; pass < pass_count; pass++) {
            if (passes[pass] == 'C') {
                vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, pipeline);
                vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, layout, 0, 1, &set, 0, NULL);
                vkCmdPushConstants(cmd, layout, VK_SHADER_STAGE_COMPUTE_BIT, 0, sizeof iterations, &iterations);
                vkCmdDispatch(cmd, invocations / 64, 1, 1);
            } else {
                VkRenderingAttachmentInfo color = {.sType = VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO,
                                                   .imageView = view,
                                                   .imageLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,
                                                   .loadOp = VK_ATTACHMENT_LOAD_OP_CLEAR,
                                                   .storeOp = VK_ATTACHMENT_STORE_OP_STORE,
                                                   .clearValue = {.color = {{0.25f, 0.5f, 0.75f, 1}}}};
                VkRenderingInfo rendering = {.sType = VK_STRUCTURE_TYPE_RENDERING_INFO,
                                             .renderArea = {{0, 0}, {2048, 2048}},
                                             .layerCount = 1,
                                             .colorAttachmentCount = 1,
                                             .pColorAttachments = &color};
                vkCmdBeginRendering(cmd, &rendering);
                vkCmdEndRendering(cmd);
            }
            barrier(cmd);
            vkCmdWriteTimestamp2(cmd, VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT, queries, pass + 1);
        }
        CHECK(vkEndCommandBuffer(cmd));
        VkSubmitInfo submit = {.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO, .commandBufferCount = 1, .pCommandBuffers = &cmd};
        CHECK(vkQueueSubmit(queue, 1, &submit, fence));
        CHECK(vkWaitForFences(device, 1, &fence, VK_TRUE, UINT64_MAX));
        CHECK(vkResetFences(device, 1, &fence));
        CHECK(vkGetQueryPoolResults(device, queries, 0, stamps, 2 * stamps * sizeof *results, results,
                                    2 * sizeof *results, VK_QUERY_RESULT_64_BIT | VK_QUERY_RESULT_WITH_AVAILABILITY_BIT));

        printf("submission %d\n", submission);
        for (uint32_t stamp = 0; stamp < stamps; stamp++) {
            uint64_t value = results[2 * stamp], available = results[2 * stamp + 1];
            const char *after = stamp == 0 ? "start" : (passes[stamp - 1] == 'C' ? "compute" : "render");
            printf("  stamp %2u after %-7s available %llu value %20llu", stamp, after, (unsigned long long)available,
                   (unsigned long long)value);
            if (stamp > 0) {
                uint64_t before = results[2 * stamp - 2];
                if (value < before) {
                    printf("  LOWER than the stamp before it");
                    faults++;
                } else {
                    printf("  +%.3f ms", (double)(value - before) * period / 1e6);
                }
            }
            if (value == 0) {
                printf("  ZERO");
                faults++;
            }
            printf("\n");
        }
    }
    printf("%d faults\n", faults);
    return faults != 0;
}
