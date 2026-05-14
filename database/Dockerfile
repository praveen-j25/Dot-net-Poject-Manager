FROM mysql:8.0

# Add low memory config for Render's 512MB free tier
RUN echo "[mysqld]\n\
performance_schema = off\n\
key_buffer_size = 16M\n\
tmp_table_size = 1M\n\
innodb_buffer_pool_size = 64M\n\
innodb_log_buffer_size = 8M\n\
max_connections = 25\n\
sort_buffer_size = 512K\n\
read_buffer_size = 256K\n\
read_rnd_buffer_size = 512K\n\
join_buffer_size = 128K\n\
thread_stack = 256K\n" > /etc/mysql/conf.d/low-memory.cnf

# Copy initialization scripts
# These scripts will run automatically when the container is created for the first time
COPY ./COMPLETE_DATABASE_SETUP.sql /docker-entrypoint-initdb.d/
COPY ./stored_procedures.sql /docker-entrypoint-initdb.d/

# Expose MySQL port
EXPOSE 3306
