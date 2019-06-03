#include <iostream>
#include <fstream>
#include <cstdlib>

#include <assimp/Importer.hpp>      // C++ importer interface
#include <assimp/scene.h>           // Output data structure
#include <assimp/postprocess.h>     // Post processing flags


const aiScene *load_assimp_file(Assimp::Importer &importer, const char *filename)
{
    return importer.ReadFile(filename,
        aiProcess_GenUVCoords |
        aiProcess_TransformUVCoords |
        aiProcess_EmbedTextures);
}

typedef uint32_t uint_t;

struct dump_scene
{
    uint_t numMaterials;
    uint_t numMeshes;
};

struct dump_material
{
    float color[4];
    char name[256];
    uint_t texWidth, texHeight;
    size_t texSize;
};

struct dump_mesh
{
    uint_t numVertices;
    uint_t numFaceVertices;
    uint_t materialIndex;
    uint_t numTextureCoords;   /* 0 or 1 */
};

struct dump_node
{
    float transformation[16];
    char name[256];
    uint_t numMeshes;
    uint_t numChildren;
};

void emit_node(std::ostream &out, aiNode *node)
{
    dump_node node1;
    node1.transformation[0] = node->mTransformation.a1;
    node1.transformation[1] = node->mTransformation.b1;
    node1.transformation[2] = node->mTransformation.c1;
    node1.transformation[3] = node->mTransformation.d1;

    node1.transformation[4] = node->mTransformation.a2;
    node1.transformation[5] = node->mTransformation.b2;
    node1.transformation[6] = node->mTransformation.c2;
    node1.transformation[7] = node->mTransformation.d2;

    node1.transformation[8] = node->mTransformation.a3;
    node1.transformation[9] = node->mTransformation.b3;
    node1.transformation[10] = node->mTransformation.c3;
    node1.transformation[11] = node->mTransformation.d3;

    node1.transformation[12] = node->mTransformation.a4;
    node1.transformation[13] = node->mTransformation.b4;
    node1.transformation[14] = node->mTransformation.c4;
    node1.transformation[15] = node->mTransformation.d4;

    strncpy_s(node1.name, node->mName.C_Str(), sizeof(node1.name));
    node1.numMeshes = node->mNumMeshes;
    node1.numChildren = node->mNumChildren;

    out.write((const char *)&node1, sizeof(node1));
    if (node->mNumMeshes > 0)
        out.write((const char *)node->mMeshes, sizeof(unsigned int) * node->mNumMeshes);

    for (uint_t j = 0; j < node->mNumChildren; j++)
        emit_node(out, node->mChildren[j]);
}

void emit_scene(std::ostream &out, const aiScene *scene)
{
    dump_scene scene1;
    scene1.numMaterials = scene->mNumMaterials;
    scene1.numMeshes = scene->mNumMeshes;
    out.write((const char *)&scene1, sizeof(scene1));

    for (uint_t i = 0; i < scene->mNumMaterials; i++)
    {
        aiMaterial *mat = scene->mMaterials[i];
        dump_material mat1;

        aiColor4D color(1, 1, 1, 1);
        mat->Get(AI_MATKEY_COLOR_DIFFUSE, color);
        mat1.color[0] = color.r;
        mat1.color[1] = color.g;
        mat1.color[2] = color.b;
        mat1.color[3] = color.a;

        aiString name;
        mat->Get(AI_MATKEY_NAME, name);
        strncpy_s(mat1.name, name.C_Str(), sizeof(mat1.name));

        const char *texdata = NULL;
        mat1.texWidth = 0;
        mat1.texHeight = 0;
        mat1.texSize = 0;

        if (mat->GetTextureCount(aiTextureType_DIFFUSE) > 0)
        {
            aiString path;
            mat->GetTexture(aiTextureType_DIFFUSE, 0, &path);
            const char *path1 = path.C_Str();
            if (path1[0] == '*')
            {
                uint_t index = (uint_t)atoi(path1 + 1);
                if (index < scene->mNumTextures)
                {
                    auto tex = scene->mTextures[index];
                    if (tex->mHeight == 0)
                    {
                        mat1.texSize = tex->mWidth;
                    }
                    else
                    {
                        mat1.texWidth = tex->mWidth;
                        mat1.texHeight = tex->mHeight;
                        mat1.texSize = tex->mWidth * tex->mHeight * 4;
                    }
                    texdata = (const char *)tex->pcData;
                }
            }
        }

        out.write((const char *)&mat1, sizeof(mat1));
        if (mat1.texSize > 0)
            out.write(texdata, mat1.texSize);
    }

    for (uint_t i = 0; i < scene->mNumMeshes; i++)
    {
        aiMesh *mesh = scene->mMeshes[i];
        dump_mesh mesh1;
        mesh1.numVertices = mesh->mNumVertices;
        mesh1.numFaceVertices = 0;
        for (uint_t j = 0; j < mesh->mNumFaces; j++)
            mesh1.numFaceVertices += 1 + mesh->mFaces[j].mNumIndices;
        mesh1.materialIndex = mesh->mMaterialIndex;
        mesh1.numTextureCoords = (mesh->mTextureCoords[0] != NULL);
        out.write((const char *)&mesh1, sizeof(mesh1));

        out.write((const char *)mesh->mVertices, sizeof(aiVector3D) * mesh->mNumVertices);
        if (mesh1.numTextureCoords)
            out.write((const char *)mesh->mTextureCoords[0], sizeof(aiVector3D) * mesh->mNumVertices);

        size_t tmpsize = sizeof(unsigned int) * mesh1.numFaceVertices;
        unsigned int *tmp = (unsigned int *)malloc(tmpsize);
        if (tmp == NULL)
        {
            std::cerr << "out of memory\n";
            exit(1);
        }
        unsigned int *p = tmp;
        for (uint_t j = 0; j < mesh->mNumFaces; j++)
        {
            auto face = mesh->mFaces[j];
            *p++ = ~face.mNumIndices;
            for (uint_t k = 0; k < face.mNumIndices; k++)
                *p++ = face.mIndices[k];
        }
        out.write((const char *)tmp, tmpsize);
        free(tmp);
    }

    emit_node(out, scene->mRootNode);
    
    out << "\n:-)";
}

int main(int argc, char **argv)
{
    if (argc != 3)
    {
        std::cerr << "syntax: " << argv[0] << " inputfile outputfile\n";
        exit(2);
    }

    Assimp::Importer importer;
    auto scene = load_assimp_file(importer, argv[1]);

    std::ofstream out(argv[2], std::ios::out | std::ios::binary);
    emit_scene(out, scene);
    out.close();

    return 0;
}


#if false
#include <nlohmann/json.hpp>
using json = nlohmann::json;

#include "DataModel_generated.h"
using namespace VRSketch4::Standalone::Data;



void write_node(flatbuffers::FlatBufferBuilder &fbb)
{
    //auto cdef = CreateCDef(builder, );
}

json write_binary_data_section(std::ostream &out, char *data, size_t datasize)
{
    auto pos = out.tellp();
    out.write(data, datasize);
    
    json j = json::object();
    j["pos"] = (uint64_t)pos;
    j["size"] = out.tellp() - pos;
    return j;
}

static const char SIGNATURE[] = {
    86, 82, 83, 107, 101, 116, 99, 104,      // VRSketch
    10, 13, 10, 0,                           // binary-vs-text problems
    0, 1, 0, 0 };

void finish_write_directory(std::ostream &out, json &directory)
{
    auto pos = out.tellp();
    out << directory;

    uint64_t directory_position = (uint64_t)pos;
    uint64_t directory_size = out.tellp() - pos;

    out.seekp(0);
    out.write(SIGNATURE, 16);
    out.write((char *)&directory_position, 8);
    out.write((char *)&directory_size, 8);
}

void write_vrsk_file(const aiScene *scene, char *filename)
{
    std::ofstream out(filename, std::ios::out | std::ios::binary);

    char header[32];
    memset(header, 0, sizeof(header));
    out.write(header, sizeof(header));

    json directory = json::object();
    finish_write_directory(out, directory);
}


int main(int argc, char **argv)
{
    if (argc != 3)
    {
        std::cerr << "syntax: " << argv[0] << " inputfile outputfile\n";
        exit(2);
    }

    Assimp::Importer importer;
    auto scene = load_assimp_file(importer, argv[1]);
    write_vrsk_file(scene, argv[2]);
    return 0;

#if false
    json j = json::object();
    json a = json::array();
    a.push_back("abc");
    a.push_back("def");
    a.push_back(12.345678901);

    j["foo"] = a;
    json k = json::object();
    j["empty"] = k;

    std::cout << j << "\n";



    flatbuffers::FlatBufferBuilder builder(16384);
    auto cdef = CreateCDef(builder);

    FinishCDefBuffer(builder, cdef);

    char *buf = (char *)builder.GetBufferPointer();
    uint32_t size = builder.GetSize();
    /*std::cout.write(buf, size);*/
    for (uint32_t i = 0; i < size; i++)
        std::cout << (int)buf[i] << " ";
    std::cout << "\n";
#endif
}
#endif
