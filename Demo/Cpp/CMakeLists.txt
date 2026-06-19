cmake_minimum_required(VERSION 3.15)
project(titan_vault_cpp_demo CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_executable(vault_demo vault_demo.cpp)

# The canonical C ABI header (shared with all the other bindings).
target_include_directories(vault_demo PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/../../Bindings/include)

# The library is loaded at runtime (LoadLibrary/dlopen) — no link against TitanVault is needed.
# On Unix, dlopen lives in libdl.
if(UNIX)
    target_link_libraries(vault_demo ${CMAKE_DL_LIBS})
endif()
